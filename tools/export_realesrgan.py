# Converts Real-ESRGAN "realesr-general-x4v3" (the small general model, 4x) to ONNX for the AI enlargement.
# The ONNX file is embedded in PhotoStudio (Assets/realesr-general-x4v3.onnx).
#
#   pip install torch onnx --index-url https://download.pytorch.org/whl/cpu
#   curl -LO https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesr-general-x4v3.pth
#   python export_realesrgan.py      -> realesr-general-x4v3.onnx
#
# Real-ESRGAN: https://github.com/xinntao/Real-ESRGAN, BSD-3-Clause licence, (c) 2021 Xintao Wang.
# The network below is SRVGGNetCompact from that repository, written out so basicsr is not needed.
import torch, torch.nn as nn, torch.nn.functional as F


class SRVGGNetCompact(nn.Module):
    def __init__(self, num_in_ch=3, num_out_ch=3, num_feat=64, num_conv=32, upscale=4):
        super().__init__()
        self.upscale = upscale
        self.body = nn.ModuleList()
        self.body.append(nn.Conv2d(num_in_ch, num_feat, 3, 1, 1))
        self.body.append(nn.PReLU(num_parameters=num_feat))
        for _ in range(num_conv):
            self.body.append(nn.Conv2d(num_feat, num_feat, 3, 1, 1))
            self.body.append(nn.PReLU(num_parameters=num_feat))
        self.body.append(nn.Conv2d(num_feat, num_out_ch * upscale * upscale, 3, 1, 1))
        self.upsampler = nn.PixelShuffle(upscale)

    def forward(self, x):
        out = x
        for layer in self.body:
            out = layer(out)
        out = self.upsampler(out)
        return out + F.interpolate(x, scale_factor=self.upscale, mode='nearest')


net = SRVGGNetCompact()
sd = torch.load('realesr-general-x4v3.pth', map_location='cpu', weights_only=False)
sd = sd.get('params_ema', sd.get('params', sd))
net.load_state_dict(sd, strict=True)
net.eval()
x = torch.rand(1, 3, 128, 128)
torch.onnx.export(net, x, 'realesr-general-x4v3.onnx', input_names=['input'], output_names=['output'], opset_version=17,
                  dynamic_axes={'input': {2: 'h', 3: 'w'}, 'output': {2: 'h', 3: 'w'}}, dynamo=False)
print('ok')
