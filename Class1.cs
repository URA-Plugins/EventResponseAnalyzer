using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Plugin;
using static EventResponseAnalyzer.i18n.ParseSingleModeCheckEventResponse;

namespace EventResponseAnalyzer
{
    public class EventResponseAnalyzer : IPlugin
    {
        [PluginDescription("解析事件效果")]
        public string Name => "EventResponseAnalyzer";
        public string Author => "离披";
        public Version Version => new(1, 0, 0);
        public string[] Targets => [];
        public async Task UpdatePlugin(ProgressContext ctx)
        {
            var progress = ctx.AddTask($"[{Name}] 更新");

            using var client = new HttpClient();
            using var resp = await client.GetAsync($"https://api.github.com/repos/URA-Plugins/{Name}/releases/latest");
            var json = await resp.Content.ReadAsStringAsync();
            var jo = JObject.Parse(json);

            var isLatest = ("v" + Version.ToString()).Equals("v" + jo["tag_name"]?.ToString());
            if (isLatest)
            {
                progress.Increment(progress.MaxValue);
                progress.StopTask();
                return;
            }
            progress.Increment(25);

            var downloadUrl = jo["assets"][0]["browser_download_url"].ToString();
            if (Config.Updater.IsGithubBlocked && !Config.Updater.ForceUseGithubToUpdate)
            {
                downloadUrl = downloadUrl.Replace("https://", "https://gh.shuise.dev/");
            }
            using var msg = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            using var stream = await msg.Content.ReadAsStreamAsync();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    break;
                progress.Increment(read / msg.Content.Headers.ContentLength ?? 1 * 0.5);
            }
            using var archive = new ZipArchive(stream);
            archive.ExtractToDirectory(Path.Combine("Plugins", Name), true);
            progress.Increment(25);

            progress.StopTask();
        }

        [PluginSetting, PluginDescription("是否在事件效果中显示当前技能灵感等级")]
        public bool ShowSkillHintLevel { get; set; } = true;
        [PluginSetting, PluginDescription("是否在事件效果中显示技能触发条件")]
        public bool ShowSkillCondition { get; set; } = false;
        Regex ExtractSkillNameRegex = new Regex("「(.*?)」");
        [Analyzer]
        public void Analyze(JObject jo)
        {
            if (!jo.HasCharaInfo()) return;
            if (jo["data"] is null || jo["data"] is not JObject data) return;
            if (data["chara_info"] is null || data["chara_info"] is not JObject chara_info) return;
            if (data.ContainsKey("unchecked_event_array"))
            {
                ParseSingleModeCheckEventResponse(jo.ToObject<Gallop.SingleModeCheckEventResponse>());
            }
        }

        public void ParseSingleModeCheckEventResponse(Gallop.SingleModeCheckEventResponse @event)
        {
            foreach (var i in @event.data.unchecked_event_array)
            {
                //收录在数据库中
                if (Database.Events.TryGetValue(i.story_id, out var story))
                {
                    var mainTree = new Tree(story.TriggerName.EscapeMarkup()); //触发者名称
                    var eventTree = new Tree($"{story.Name.EscapeMarkup()}({i.story_id})"); //事件名称
                    var eventHasManualChoice = (i.event_contents_info.choice_array.Length >= 2);   // 是否有手动选项。有选项的不能获取结果了
                    for (var j = 0; j < i.event_contents_info.choice_array.Length; ++j)
                    {
                        var originalChoice = new Choice();
                        if (story.Choices.Count < (j + 1))
                        {
                            originalChoice.Option = string.Format(I18N_UnknownOption, i.event_contents_info.choice_array[j].select_index);
                            originalChoice.SuccessEffect = I18N_UnknownEffect;
                            originalChoice.FailedEffect = I18N_UnknownEffect;
                        }
                        else
                        {
                            originalChoice = story.Choices[j][0]; //因为kamigame的事件无法直接根据SelectIndex区分成功与否，所以必然只会有一个Choice;
                        }
                        if (ShowSkillHintLevel || ShowSkillCondition)
                        {
                            var successSkillNames = ExtractSkillNameRegex.Matches(originalChoice.SuccessEffect);
                            foreach (var matchedSkillName in successSkillNames.Cast<Match>())
                            {
                                var skillName = matchedSkillName.Groups[1].Value;
                                var skill = SkillManagerGenerator.Default.GetSkillByName(skillName);
                                if (skill != null)
                                {
                                    if (ShowSkillHintLevel)
                                    {
                                        var tip = @event.data.chara_info.skill_tips_array.FirstOrDefault(x => x.group_id == skill.GroupId && x.rarity == skill.Rarity);
                                        originalChoice.SuccessEffect = originalChoice.SuccessEffect.Replace(skillName, $"{skillName}(Lv.{TipColor(tip?.level ?? 0)})");
                                    }
                                    if (ShowSkillCondition && skill.Propers.Length != 0)
                                    {
                                        originalChoice.SuccessEffect = originalChoice.SuccessEffect.Replace(skillName, $"{skillName}[[{SkillCondition(skill.Propers)}]]");
                                    }
                                }
                            }
                            var failSkillNames = ExtractSkillNameRegex.Matches(originalChoice.FailedEffect);
                            foreach (var matchedSkillName in failSkillNames.Cast<Match>())
                            {
                                var skillName = matchedSkillName.Groups[1].Value;
                                var skill = SkillManagerGenerator.Default.GetSkillByName(skillName);
                                if (skill != null)
                                {
                                    if (ShowSkillHintLevel)
                                    {
                                        var tip = @event.data.chara_info.skill_tips_array.FirstOrDefault(x => x.group_id == skill.GroupId && x.rarity == skill.Rarity);
                                        originalChoice.FailedEffect = originalChoice.FailedEffect.Replace(skillName, $"{skillName}(Lv.{TipColor(tip?.level ?? 0)})");
                                    }
                                    if (ShowSkillCondition && skill.Propers.Length != 0)
                                    {
                                        originalChoice.FailedEffect = originalChoice.FailedEffect.Replace(skillName, $"{skillName}[[{SkillCondition(skill.Propers)}]]");
                                    }
                                }
                            }
                            string TipColor(int level) => level switch
                            {
                                <= 0 => $"[red]{level}[/]",
                                < 5 => $"[yellow]{level}[/]",
                                >= 5 => $"[green]{level}[/]"
                            };
                            string SkillCondition(SkillProper[] propers)
                            {
                                var sb = new System.Text.StringBuilder();
                                for (var i = 0; i < propers.Length; i++)
                                {
                                    var proper = propers[i];
                                    if (i != 0) sb.Append('/');
                                    if (proper.Style != SkillProper.StyleType.None)
                                    {
                                        sb.Append(StyleToText(proper.Style));
                                    }
                                    if (proper.Distance != SkillProper.DistanceType.None)
                                    {
                                        if (sb.Length != 0 && sb[^1] != '/') sb.Append('・');
                                        sb.Append(DistanceToText(proper.Distance));
                                    }
                                    if (proper.Ground != SkillProper.GroundType.None)
                                    {
                                        if (sb.Length != 0 && sb[^1] != '/') sb.Append('・');
                                        sb.Append(GroundToText(proper.Ground));
                                    }
                                }
                                return sb.ToString();
                                string StyleToText(SkillProper.StyleType style) => style switch
                                {
                                    SkillProper.StyleType.Nige => "逃",
                                    SkillProper.StyleType.Senko => "先",
                                    SkillProper.StyleType.Sashi => "差",
                                    SkillProper.StyleType.Oikomi => "追"
                                };
                                string DistanceToText(SkillProper.DistanceType distance) => distance switch
                                {
                                    SkillProper.DistanceType.Short => "短",
                                    SkillProper.DistanceType.Mile => "英",
                                    SkillProper.DistanceType.Middle => "中",
                                    SkillProper.DistanceType.Long => "长"
                                };
                                string GroundToText(SkillProper.GroundType ground) => ground switch
                                {
                                    SkillProper.GroundType.Dirt => "泥",
                                    SkillProper.GroundType.Turf => "芝"
                                };
                            }
                        }
                        //显示选项
                        var tree = new Tree($"{(string.IsNullOrEmpty(originalChoice.Option) ? I18N_NoOption : originalChoice.Option)}".EscapeMarkup());
                        //如果没有失败效果则显示成功效果（别问我为什么这么设置，问kamigame
                        if (string.IsNullOrEmpty(originalChoice.FailedEffect))
                            tree.AddNode(originalChoice.SuccessEffect);
                        else if (originalChoice.SuccessEffect == I18N_UnknownEffect && originalChoice.FailedEffect == I18N_UnknownEffect)
                            tree.AddNode(MarkupText(I18N_UnknownEffect, State.None));
                        else
                            tree.AddNode($"[mediumspringgreen on #081129]({I18N_WhenSuccess}){originalChoice.SuccessEffect}[/]{Environment.NewLine}[#FF0050 on #081129]({I18N_WhenFail}){originalChoice.FailedEffect}[/]{Environment.NewLine}");
                        eventTree.AddNode(tree);

                        string MarkupText(string text, State state)
                        {
                            return state switch
                            {
                                State.Unknown => $"[darkorange on #081129]{text}({I18N_Unknown})[/]", //未知的
                                State.Fail => $"[#FF0050 on #081129]{text}[/]", //失败
                                State.Success => $"[mediumspringgreen on #081129]{text}[/]", //成功
                                State.GreatSuccess => $"[lightgoldenrod1 on #081129]{text}[/]", //大成功
                                State.None => $"[#afafaf on #081129]{text}[/]", //中性
                                _ => throw new NotImplementedException()
                            };
                        }
                    }
                    mainTree.AddNode(eventTree);
                    AnsiConsole.Write(mainTree);
                }
                else //未知事件，直接显示ChoiceIndex
                {
                    var mainTree = new Tree(I18N_UnknownSource);
                    var eventTree = new Tree($"{I18N_UnknownEvent}({i?.story_id})");
                    for (var j = 0; j < i?.event_contents_info?.choice_array.Length; ++j)
                    {
                        var tree = new Tree(string.Format(I18N_UnknownOption, i.event_contents_info.choice_array[j].select_index));
                        tree.AddNode(I18N_UnknownEffect);
                        eventTree.AddNode(tree);
                    }
                    mainTree.AddNode(eventTree);
                    AnsiConsole.Write(mainTree);
                }
            }
        }
    }
}
