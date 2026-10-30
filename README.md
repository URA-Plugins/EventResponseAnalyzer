# EventResponseAnalyzer

解析 single-mode `check_event`、`exec_command` 和 `load` 响应中的事件选项与效果，并在 `EventResponseAnalyzer` workspace 中显示结果。事件树在发布前冻结为不可变快照。

## History

事件记录按 `single_mode_chara_id`、`turn`、`story_id` 唯一标识。同一记录再次输出时原位更新；一次响应中的多个事件可分别浏览。

- `↑` / `↓`：上一条（更旧）/ 下一条（更新）
- `←` / `→`：最旧 / 最新
- `PageUp` / `PageDown`、`Home` / `End`、鼠标滚轮：滚动当前事件正文

History 仅在当前进程内保存。配置文件为 `PluginData/EventResponseAnalyzer/settings.json`：

```json
{
  "historyLimit": 100
}
```

`historyLimit` 的有效范围是 `0` 到 `1000`，默认值为 `100`；设为 `0` 时禁用 History，并按响应批次显示事件。

## 构建

```powershell
git -c core.longpaths=true submodule update --init --recursive
dotnet build .\EventResponseAnalyzer.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"
```

## 验证与发布

`tests/EventResponseAnalyzer.Tests` 覆盖 DTO 投影、端点分发、事件选项与颜色、历史浏览、严格配置及保存/取消事务、工作区清理，并加载本轮 ZIP 验证真实事件面板、程序集来源及关闭清理。

测试使用 `UraTestHostRoot` 指定匹配的 Host 源码；生产插件继续引用 NuGet。默认入口先执行本仓发布边界检查。运行前将 `URA_PLUGIN_SMOKE_PACKAGE_ROOT` 设为本轮构建 ZIP 所在目录，按 manifest 的 `InternalName` 唯一匹配；缺包或重复包明确失败。GitHub Actions 未设置该变量时使用 `GITHUB_WORKSPACE/artifacts`。`--package-load` 单独运行 ZIP 验证。本仓测试工具位于 `tests/Support`。

`tests/EventResponseAnalyzerReplaySmoke` 默认运行捕获解析与三语言错误识别自测；真实回放需传 `--corpus <目录>`。目录顶层 `*.txt` 必须以 `[整数序号]` 开头，包含 notify HTTP 头和原始 body。空语料、长度或 URL/SID 配对错误、未知端点、分发异常及可见 Error 均失败。Error 检查仅针对当前 framebuffer 的可见内容，不检查屏幕外历史或其他 workspace 的日志。真实捕获和临时回放输出不入库。

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。
