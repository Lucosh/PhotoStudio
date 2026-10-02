# Converts LaMa (big-lama, Apache 2.0) to ONNX for "Rimuovi con AI", at any image size (multiple of 8), so that
# the network sees each crop at its real resolution instead of a reduced 512 x 512 copy.
# The ONNX file is published as lama-hd.onnx in the "models-1" release; Core/AiModels.cs checks its SHA-256.
#
#   git clone --depth 1 https://github.com/Carve-Photos/lama.git && cd lama
#   curl -LO https://huggingface.co/smartywu/big-lama/resolve/main/big-lama.zip && unzip big-lama.zip
#   pip install torch onnx onnxscript onnxruntime omegaconf kornia pyyaml easydict --index-url https://download.pytorch.org/whl/cpu
#   python export_lama.py      -> lama-hd.onnx (and a check against PyTorch's own FFT)
#
# The Fourier units of LaMa are rewritten as products with DFT matrices built from the size of the input, all on
# 4-D tensors: ONNX Runtime runs them fast, also on the graphics card (DirectML), and the size stays free.
# Only the generator is built, so the training code (PyTorch Lightning) is not needed.
# LaMa: https://github.com/advimman/lama, Apache-2.0, (c) Samsung AI Center and the LaMa authors.
import math
import pickle
import sys
import types

import numpy as np
import torch
import yaml
from omegaconf import OmegaConf

# saicinpainting.utils imports PyTorch Lightning for training only: a stand-in is enough to build the generator.
sys.modules.setdefault('pytorch_lightning', types.SimpleNamespace(seed_everything=lambda *a, **k: None))
from saicinpainting.training.modules import make_generator
from saicinpainting.training.modules import ffc


def _dft(n, device):
    """cos and sin of 2*pi*j*k/n, n x n (symmetric)."""
    i = torch.arange(n, device=device, dtype=torch.float32)
    angle = (2 * math.pi / n) * i[:, None] * i[None, :]
    return torch.cos(angle), torch.sin(angle)


def _inv_sqrt(n, ref):
    """1 / sqrt(n) as a tensor, also when n is a symbolic size during the export."""
    return torch.rsqrt(torch.ones((), dtype=ref.dtype, device=ref.device) * n)


def rfft2_ortho(x):
    """Same as torch.fft.rfftn(x, dim=(-2, -1), norm='ortho'), as (real, imag), with 4-D matmuls only."""
    h, w = x.shape[-2], x.shape[-1]
    k = w // 2 + 1
    cw, sw = _dft(w, x.device)
    ar = x @ cw[:, :k]                      # along the width: real input -> half spectrum
    ai = -(x @ sw[:, :k])
    ch, sh = _dft(h, x.device)
    tr, ti = ar.transpose(-1, -2), ai.transpose(-1, -2)   # (b, c, k, h): along the height on the last axis
    br = tr @ ch + ti @ sh                  # (a)(cos - i sin) with a = tr + i ti
    bi = ti @ ch - tr @ sh
    scale = _inv_sqrt(h * w, x)
    return br.transpose(-1, -2) * scale, bi.transpose(-1, -2) * scale


def irfft2_ortho(xr, xi, h, w):
    """Same as torch.fft.irfftn(x, s=(h, w), dim=(-2, -1), norm='ortho') for the half spectrum (real, imag)."""
    ch, sh = _dft(h, xr.device)
    tr, ti = xr.transpose(-1, -2), xi.transpose(-1, -2)
    ar = tr @ ch - ti @ sh                  # inverse along the height: (a)(cos + i sin)
    ai = ti @ ch + tr @ sh
    ar, ai = ar.transpose(-1, -2), ai.transpose(-1, -2)   # (b, c, h, k)
    k = w // 2 + 1
    cw, sw = _dft(w, xr.device)
    j = torch.arange(k, device=xr.device, dtype=torch.float32)
    # Hermitian weights: the DC term (and the Nyquist one for an even width) once, the others twice.
    weight = torch.where((j == 0) | ((j * 2) == w), torch.ones_like(j), 2 * torch.ones_like(j))[:, None]
    out = ar @ (weight * cw[:k, :]) - ai @ (weight * sw[:k, :])
    return out * _inv_sqrt(h * w, xr)


def fourier_forward(self, x):
    """
    FourierUnit.forward of LaMa with the FFTs above (no spatial scaling, no 3-D FFT in big-lama). The original
    interleaves real and imaginary channels through a 5-D tensor; here they are concatenated (all real parts, then
    all imaginary ones) and the 1 x 1 convolution and its batch norm use the same order (see reorder_channels).
    """
    h, w = x.shape[-2], x.shape[-1]
    real, imag = rfft2_ortho(x)
    ffted = self.relu(self.bn(self.conv_layer(torch.cat([real, imag], dim=1))))
    c = ffted.shape[1] // 2
    return irfft2_ortho(ffted[:, :c], ffted[:, c:], h, w)


def reorder_channels(unit):
    """From interleaved (r0, i0, r1, i1...) to concatenated (r0, r1... i0, i1...) channels, inputs and outputs."""
    conv, bn = unit.conv_layer, unit.bn
    assert conv.groups == 1 and conv.kernel_size == (1, 1)
    def order(n):
        half = n // 2
        return torch.tensor([2 * c + t for t in range(2) for c in range(half)])
    out_order, in_order = order(conv.out_channels), order(conv.in_channels)
    with torch.no_grad():
        conv.weight.copy_(conv.weight[out_order][:, in_order])
        for name in ('weight', 'bias', 'running_mean', 'running_var'):
            t = getattr(bn, name)
            t.copy_(t[out_order])


class Lama(torch.nn.Module):
    def __init__(self, generator):
        super().__init__()
        self.generator = generator

    def forward(self, image, mask):
        masked = image * (1 - mask)
        predicted = self.generator(torch.cat([masked, mask], dim=1))
        inpainted = mask * predicted + (1 - mask) * image
        return torch.clamp(inpainted * 255, min=0, max=255)


class _Anything:
    def __init__(self, *a, **k): pass
    def __setstate__(self, state): pass


class _Unpickler(pickle.Unpickler):
    # The checkpoint also pickles Lightning's training callbacks: they are read as empty objects.
    def find_class(self, module, name):
        return _Anything if module.startswith('pytorch_lightning') else super().find_class(module, name)


def build(rewritten):
    config = OmegaConf.create(yaml.safe_load(open('big-lama/config.yaml')))
    kwargs = dict(config.generator)
    kind = kwargs.pop('kind')
    generator = make_generator(config, kind, **kwargs)
    pickle_module = types.SimpleNamespace(Unpickler=_Unpickler, load=pickle.load, __name__='pickle')
    state = torch.load('big-lama/models/best.ckpt', map_location='cpu', weights_only=False, pickle_module=pickle_module)['state_dict']
    generator.load_state_dict({k[len('generator.'):]: v for k, v in state.items() if k.startswith('generator.')}, strict=True)
    if rewritten:
        for m in generator.modules():
            if isinstance(m, ffc.FourierUnit):
                reorder_channels(m)
                m.forward = types.MethodType(fourier_forward, m)
    return Lama(generator).eval()


if __name__ == '__main__':
    original, model = build(False), build(True)
    image = torch.rand(1, 3, 512, 512)
    mask = (torch.rand(1, 1, 512, 512) > 0.7).float()
    h, w = torch.export.Dim('h', min=8, max=1024), torch.export.Dim('w', min=8, max=1024)
    torch.onnx.export(model, (image, mask), 'lama-hd-split.onnx', input_names=['image', 'mask'], output_names=['output'],
                      dynamo=True, opset_version=18, dynamic_shapes={'image': {2: 8 * h, 3: 8 * w}, 'mask': {2: 8 * h, 3: 8 * w}})
    import onnx
    onnx.save_model(onnx.load('lama-hd-split.onnx', load_external_data=True), 'lama-hd.onnx', save_as_external_data=False)

    # Check against PyTorch's own FFT (the original network) at several sizes, square and not, even and odd.
    import onnxruntime as ort
    session = ort.InferenceSession('lama-hd.onnx', providers=['CPUExecutionProvider'])
    rng = np.random.default_rng(1)
    for hh, ww in [(512, 512), (768, 1280), (1000, 664)]:
        img = rng.random((1, 3, hh, ww), dtype=np.float32)
        msk = np.zeros((1, 1, hh, ww), np.float32)
        msk[:, :, hh // 3: 2 * hh // 3, ww // 3: 2 * ww // 3] = 1
        out = session.run(['output'], {'image': img, 'mask': msk})[0]
        with torch.no_grad():
            ref = original(torch.from_numpy(img), torch.from_numpy(msk)).numpy()
        print(f'{hh} x {ww}: largest difference from the original network {np.abs(out - ref).max():.4f} (of 255)')
