# 第三方组件声明

本项目自身代码以 MIT 许可发布（见 `LICENSE`）。运行所需的第三方组件如下，其许可条款与本项目不同。

---

## 1. LibreHardwareMonitorLib 0.9.4

- **文件**：`LibreHardwareMonitorLib.dll`
- **用途**：读取 CPU / GPU 传感器数据（温度、负载、频率）
- **项目主页**：https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- **许可**：Mozilla Public License 2.0（全文见 `LICENSES/MPL-2.0.txt`）
- **修改情况**：**未做任何修改**，直接使用 NuGet 包 `LibreHardwareMonitorLib 0.9.4` 中 `lib/net472` 的原始二进制。

MPL-2.0 允许在闭源或开源项目中引用、并可原样再分发该二进制；
若修改了 LibreHardwareMonitor 自身的源码，则修改部分需按 MPL-2.0 公开。
本项目未修改其源码，因此只需保留本声明与许可全文。

如需自行核对，可用官方 NuGet 包替换：

```powershell
nuget install LibreHardwareMonitorLib -Version 0.9.4 -OutputDirectory packages
# 取 packages\LibreHardwareMonitorLib.0.9.4\lib\net472\LibreHardwareMonitorLib.dll
```

## 2. WinRing0 驱动（随 LibreHardwareMonitorLib 分发）

- **形式**：以压缩资源（`LibreHardwareMonitor.Resources.WinRing0x64.gz`）内嵌在 `LibreHardwareMonitorLib.dll` 中，首次以管理员权限运行时释放为 `<exe名>.sys` 并加载
- **用途**：通过内核层读取 CPU 的 MSR / EC 寄存器（CPU 温度的唯一可行途径）
- **来源**：OpenLibSys / WinRing0，项目地址 http://openlibsys.org/
- **许可**：BSD 3-Clause（OpenLibSys 原始条款）

> 注意：WinRing0 存在历史漏洞 [CVE-2020-14979](https://nvd.nist.gov/vuln/detail/CVE-2020-14979)，
> 自 2025 年 3 月起被 Microsoft Defender 归类为 HackTool 并可能被拦截。
> 这与本项目的功能实现无关，属于该驱动自身的历史问题，详见 README 的「杀毒软件与驱动拦截」一节。

本项目不直接分发 WinRing0 二进制，也不修改它。

---

## 3. 运行时 / 工具链

| 组件 | 用途 | 许可 |
|---|---|---|
| .NET Framework 4.8（系统自带） | 运行时 | Microsoft 最终用户许可 |
| csc.exe（Windows 自带 C# 编译器） | 构建 | Microsoft 最终用户许可 |
| Pillow（仅 `tools/gen_icon.py` 生成图标时使用，不参与分发） | 生成 app.ico | MIT-CMU |
