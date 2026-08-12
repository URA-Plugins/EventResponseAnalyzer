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
