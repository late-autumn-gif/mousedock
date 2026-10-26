# 代码签名与「杀软误报」处理

先说结论，避免走弯路：

## 三条路，效果完全不同

| 方案 | 费用 | 效果 |
|---|---|---|
| **自签名证书** | 0 | ❌ 对别人无效。证书根不在受信任列表，别人电脑上依旧弹「未知发布者」，部分杀软反而更敏感 |
| **SignPath Foundation** | 0（需申请，面向开源） | ✅ 正规 OV 证书签名，SmartScreen 不再提示未知发布者 |
| **Azure Trusted Signing** | 约 $9.9/月 | ✅ 同上，签发快，个人/小团队可用 |
| **商业 OV / EV 证书** | $200–500/年 | ✅ OV 需积累信誉；**EV 立即获得 SmartScreen 信誉** |

**没有第四条路**：不签名 + 新程序，SmartScreen 一定提示；这是信誉机制，不是靠改代码能绕过的。

## 推荐路径（免费 + 开源）

1. 仓库开源后，向 [SignPath Foundation](https://signpath.org/) 提交申请（需要项目有一定活跃度、OSI 许可）
2. 通过后在 GitHub Actions 里加签名步骤：

```yaml
- uses: signpath/github-action-submit-signing-request@v1
  with:
    api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
    organization-id: ${{ secrets.SIGNPATH_ORG_ID }}
    project-slug: mousedock
    signing-policy-slug: release-signing
    artifact-configuration-slug: exe
    github-artifact-id: ${{ steps.upload.outputs.artifact-id }}
    wait-for-completion: true
    output-artifact-directory: signed
```

`.github/workflows/release.yml` 里已经留好了这段的注释位置。

## 降低误报的实际手段（不依赖证书）

按有效性排序：

1. **提交微软复核**：<https://www.microsoft.com/wdsi/filesubmission> 选择 "Incorrectly detected as malware"，通常 24–72 小时出结果，会同步到 Defender
2. **附上源码链接与构建脚本**：报告里写清「开源项目，源码在此，可复现构建」
3. **不要加壳/混淆**：本项目刻意保持 IL 可读
4. **不要做自动更新与自解压落盘**：本项目没有
5. **给每个 Release 附 SHA-256**：让用户能校验，也便于安全厂商比对
6. **单文件自解压**会被少数引擎判为 "dropper"，如遇到可提供 `--no-single-file` 的多文件构建作为替代下载

## 其它厂商误报

- 卡巴斯基：<https://opentip.kaspersky.com/> → False positive
- 火绒 / 360：官网「误报反馈」表单
- VirusTotal 上的误报可逐个点进厂商页面提交

## 本仓库提供的脚本

`scripts/sign.ps1` 是签名模板，接自己的证书即可：

```powershell
./scripts/sign.ps1 -Exe dist\MouseDock.exe -Pfx cert.pfx -Password $env:PFX_PASS
```

签名后务必验证：

```powershell
Get-AuthenticodeSignature dist\MouseDock.exe | Format-List Status, SignerCertificate
```
