using Gallop;
using Gallop.Endpoints;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text.RegularExpressions;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;
using static EventResponseAnalyzer.i18n.ParseSingleModeCheckEventResponse;

namespace EventResponseAnalyzer
{
    public class EventResponseAnalyzer : IPlugin
    {
        const string WorkspaceTitle = "EventResponseAnalyzer";
        const string EventsPanelKey = "events";

        static readonly Regex ExtractSkillNameRegex = new("「(.*?)」");

        ILiveDisplayOutput? liveDisplay;
        LiveDisplayWorkspace? workspace;

        public string Name => "EventResponseAnalyzer";
        public string Author => "离披";
        public string[] Targets => [];

        public void Initialize(IPluginContext context)
        {
            liveDisplay = context.LiveDisplay;
            workspace = LiveDisplay.CreateWorkspace(WorkspaceTitle);
        }

        public Task UpdatePlugin(ProgressContext ctx) => Task.CompletedTask;

        [ResponseAnalyzer<GameApi.SingleMode.CheckEvent>]
        public ValueTask Analyze(SingleModeCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeArc.CheckEvent>]
        public ValueTask Analyze(SingleModeArcCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeBreeders.CheckEvent>]
        public ValueTask Analyze(SingleModeBreedersCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeCook.CheckEvent>]
        public ValueTask Analyze(SingleModeCookCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeFree.CheckEvent>]
        public ValueTask Analyze(SingleModeFreeCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeLegend.CheckEvent>]
        public ValueTask Analyze(SingleModeLegendCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeLive.CheckEvent>]
        public ValueTask Analyze(SingleModeLiveCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeMecha.CheckEvent>]
        public ValueTask Analyze(SingleModeMechaCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeOnsen.CheckEvent>]
        public ValueTask Analyze(SingleModeOnsenCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModePioneer.CheckEvent>]
        public ValueTask Analyze(SingleModePioneerCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeRamen.CheckEvent>]
        public ValueTask Analyze(SingleModeRamenCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeSport.CheckEvent>]
        public ValueTask Analyze(SingleModeSportCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeTeam.CheckEvent>]
        public ValueTask Analyze(SingleModeTeamCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        [ResponseAnalyzer<GameApi.SingleModeVenus.CheckEvent>]
        public ValueTask Analyze(SingleModeVenusCheckEventResponse response)
            => Analyze(response.data?.chara_info, response.data?.unchecked_event_array);

        ValueTask Analyze(SingleModeChara? charaInfo, SingleModeEventInfo[]? uncheckedEvents)
        {
            if (charaInfo == null || uncheckedEvents == null) return ValueTask.CompletedTask;

            ParseSingleModeCheckEventResponse(charaInfo, uncheckedEvents);
            return ValueTask.CompletedTask;
        }

        public void ParseSingleModeCheckEventResponse(SingleModeChara charaInfo, SingleModeEventInfo[] uncheckedEvents)
        {
            var trees = new List<IRenderable>();
            foreach (var i in uncheckedEvents)
                trees.Add(Database.Events.TryGetValue(i.story_id, out var story)
                    ? BuildKnownEventTree(charaInfo, i, story)
                    : BuildUnknownEventTree(i));

            if (trees.Count > 0)
                LiveDisplay.SetPanel(Workspace, EventsPanelKey, "事件分析", new Rows(trees), fullBleed: true);
        }

        static Tree BuildKnownEventTree(SingleModeChara charaInfo, SingleModeEventInfo eventInfo, Story story)
        {
            var mainTree = new Tree(story.TriggerName.EscapeMarkup());
            var eventTree = new Tree($"{story.Name.EscapeMarkup()}({eventInfo.story_id})");

            for (var j = 0; j < eventInfo.event_contents_info.choice_array.Length; ++j)
            {
                var originalChoice = GetStoryChoice(story, eventInfo.event_contents_info.choice_array[j], j);

                originalChoice.SuccessEffect = EnrichSkillEffect(originalChoice.SuccessEffect, charaInfo);
                originalChoice.FailedEffect = EnrichSkillEffect(originalChoice.FailedEffect, charaInfo);

                var tree = new Tree((string.IsNullOrEmpty(originalChoice.Option)
                    ? I18N_NoOption
                    : originalChoice.Option).EscapeMarkup());

                if (string.IsNullOrEmpty(originalChoice.FailedEffect))
                    tree.AddNode(originalChoice.SuccessEffect);
                else if (originalChoice.SuccessEffect == I18N_UnknownEffect && originalChoice.FailedEffect == I18N_UnknownEffect)
                    tree.AddNode($"[#afafaf on #081129]{I18N_UnknownEffect}[/]");
                else
                    tree.AddNode($"[mediumspringgreen on #081129]({I18N_WhenSuccess}){originalChoice.SuccessEffect}[/]{Environment.NewLine}[#FF0050 on #081129]({I18N_WhenFail}){originalChoice.FailedEffect}[/]{Environment.NewLine}");

                eventTree.AddNode(tree);
            }

            mainTree.AddNode(eventTree);
            return mainTree;
        }

        static Tree BuildUnknownEventTree(SingleModeEventInfo eventInfo)
        {
            var mainTree = new Tree(I18N_UnknownSource);
            var eventTree = new Tree($"{I18N_UnknownEvent}({eventInfo.story_id})");

            foreach (var choice in eventInfo.event_contents_info.choice_array)
            {
                var tree = new Tree(string.Format(I18N_UnknownOption, FirstSelectIndex(choice)));
                tree.AddNode(I18N_UnknownEffect);
                eventTree.AddNode(tree);
            }

            mainTree.AddNode(eventTree);
            return mainTree;
        }

        static Choice GetStoryChoice(Story story, ChoiceArray choice, int index)
        {
            if (story.Choices.Count <= index || story.Choices[index].Count == 0)
                return new Choice
                {
                    Option = string.Format(I18N_UnknownOption, FirstSelectIndex(choice)),
                    SuccessEffect = I18N_UnknownEffect,
                    FailedEffect = I18N_UnknownEffect,
                };

            var originalChoice = story.Choices[index][0];
            return new Choice
            {
                Option = originalChoice.Option,
                SuccessEffect = originalChoice.SuccessEffect,
                FailedEffect = originalChoice.FailedEffect,
            };
        }

        static string EnrichSkillEffect(string effect, SingleModeChara charaInfo)
        {
            foreach (Match matchedSkillName in ExtractSkillNameRegex.Matches(effect))
            {
                var skillName = matchedSkillName.Groups[1].Value;
                var skill = SkillManagerGenerator.Default.GetSkillByName(skillName);
                if (skill == null)
                    continue;

                var tip = charaInfo.skill_tips_array?.FirstOrDefault(x => x.group_id == skill.GroupId && x.rarity == skill.Rarity);
                effect = effect.Replace(skillName, $"{skillName}(Lv.{TipColor(tip?.level ?? 0)})");
            }

            return effect;
        }

        static int FirstSelectIndex(ChoiceArray choice)
        {
            return choice.select_index_info_array?.FirstOrDefault()?.select_index ?? 0;
        }

        static string TipColor(int level) => level switch
        {
            <= 0 => $"[red]{level}[/]",
            < 5 => $"[yellow]{level}[/]",
            >= 5 => $"[green]{level}[/]",
        };

        ILiveDisplayOutput LiveDisplay => liveDisplay
            ?? throw new InvalidOperationException("EventResponseAnalyzer 尚未初始化 LiveDisplay。");

        LiveDisplayWorkspace Workspace => workspace
            ?? throw new InvalidOperationException("EventResponseAnalyzer 尚未创建 LiveDisplay workspace。");
    }
}
