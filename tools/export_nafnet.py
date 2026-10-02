# Converts the NAFNet weights used by the AI noise reduction and refocus to ONNX.
# They are published as the "models-1" release of PhotoStudio, and Core/AiModels.cs checks their SHA-256.
#
#   pip install torch onnx onnxscript --index-url https://download.pytorch.org/whl/cpu
#   curl -LO https://raw.githubusercontent.com/megvii-research/NAFNet/main/basicsr/models/archs/NAFNet_arch.py
#   curl -L -o sidd32.pth  https://huggingface.co/nyanko7/nafnet-models/resolve/main/NAFNet-SIDD-width32.pth
#   curl -L -o gopro32.pth https://huggingface.co/nyanko7/nafnet-models/resolve/main/NAFNet-GoPro-width32.pth
#   python export_nafnet.py      -> nafnet-sidd32.onnx, nafnet-gopro32.onnx
#
# NAFNet: https://github.com/megvii-research/NAFNet, MIT licence, (c) 2022 megvii-model.
import sys, types, torch, torch.nn as nn
# The NAFNet code imports LayerNorm2d from basicsr: an equivalent made of plain ops, which ONNX can export.
class LayerNorm2d(nn.Module):
    def __init__(self, channels, eps=1e-6):
        super().__init__()
        self.register_parameter('weight', nn.Parameter(torch.ones(channels)))
        self.register_parameter('bias', nn.Parameter(torch.zeros(channels)))
        self.eps = eps
    def forward(self, x):
        mu = x.mean(1, keepdim=True)
        var = (x - mu).pow(2).mean(1, keepdim=True)
        y = (x - mu) / (var + self.eps).sqrt()
        return self.weight.view(1, -1, 1, 1) * y + self.bias.view(1, -1, 1, 1)
for name in ['basicsr', 'basicsr.models', 'basicsr.models.archs']:
    sys.modules[name] = types.ModuleType(name)
au = types.ModuleType('basicsr.models.archs.arch_util'); au.LayerNorm2d = LayerNorm2d
sys.modules['basicsr.models.archs.arch_util'] = au
la = types.ModuleType('basicsr.models.archs.local_arch'); la.Local_Base = object
sys.modules['basicsr.models.archs.local_arch'] = la
src = open('NAFNet_arch.py').read().split('class NAFNetLocal')[0]
ns = {}; exec(src, ns)
cfgs = {
  'sidd32': dict(width=32, enc_blk_nums=[2,2,4,8], middle_blk_num=12, dec_blk_nums=[2,2,2,2]),
  'gopro32': dict(width=32, enc_blk_nums=[1,1,1,28], middle_blk_num=1, dec_blk_nums=[1,1,1,1]),
}
for key, cfg in cfgs.items():
    net = ns['NAFNet'](img_channel=3, **cfg)
    sd = torch.load(key + '.pth', map_location='cpu', weights_only=False)
    sd = sd.get('params', sd)
    net.load_state_dict(sd, strict=True); net.eval()
    x = torch.rand(1, 3, 512, 512)
    torch.onnx.export(net, x, f'nafnet-{key}.onnx', input_names=['input'], output_names=['output'], opset_version=17,
                      dynamic_axes={'input': {2: 'h', 3: 'w'}, 'output': {2: 'h', 3: 'w'}}, dynamo=False)
    print(key, 'ok')
