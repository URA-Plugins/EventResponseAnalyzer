using Gallop;
using Gallop.Endpoints;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using static EventResponseAnalyzer.i18n.ParseSingleModeCheckEventResponse;

namespace EventResponseAnalyzer
{
    public class EventResponseAnalyzer : IPlugin
    {
        const string WorkspaceTitle = "EventResponseAnalyzer";
        const string EventsPanelKey = "events";
        const int EventDisplayPriority = 2;
        const int LegendBuffSelectionPlayingState = 5;
        const int LegendBuffSelectionStoryId = 400010112;

        static readonly Regex ExtractSkillNameRegex = new("「(.*?)」");

        readonly object stateGate = new();
        Workspace? workspace;

        public void Initialize(IPluginContext context)
        {
            context.Analyzers.Register<SingleModeCheckEventResponse>(
                AnalyzerKind.Response,
                [EndpointPattern.Regex(@"^/umamusume/single_mode(?:_[^/]+)?/(?:check_event|exec_command)$")],
                invocation => Analyze(invocation.Endpoint, invocation.Payload),
                EventDisplayPriority);
            context.Analyzers.Register<SingleModeLoadResponse>(
                AnalyzerKind.Response,
                [EndpointPattern.Wildcard("/umamusume/single_mode*/load")],
                invocation => Analyze(invocation.Endpoint, invocation.Payload),
                EventDisplayPriority);
        }

        public void Dispose()
        {
            lock (stateGate)
            {
                if (workspace == null)
                    return;

                workspace.RemovePanel(EventsPanelKey);
                workspace = null;
            }
        }

        ValueTask Analyze(GameEndpointDescriptor endpoint, SingleModeCheckEventResponse response)
            => IsLegendEndpoint(endpoint)
                ? AnalyzeLegend(response.data.chara_info, response.data.unchecked_event_array)
                : Analyze(response.data.chara_info, response.data.unchecked_event_array);

        ValueTask Analyze(GameEndpointDescriptor endpoint, SingleModeLoadResponse response)
        {
            var loadCommon = response.data.single_mode_load_common;
            return IsLegendEndpoint(endpoint)
                ? AnalyzeLegend(loadCommon.chara_info, loadCommon.unchecked_event_array)
                : Analyze(loadCommon.chara_info, loadCommon.unchecked_event_array);
        }

        static bool IsLegendEndpoint(GameEndpointDescriptor endpoint)
            => endpoint.Path.StartsWith("/umamusume/single_mode_legend/", StringComparison.Ordinal);

        ValueTask AnalyzeLegend(SingleModeChara charaInfo, SingleModeEventInfo[] uncheckedEvents)
            => Analyze(
                charaInfo,
                uncheckedEvents,
                switchToWorkspace: !IsLegendBuffSelection(charaInfo, uncheckedEvents));

        ValueTask Analyze(
            SingleModeChara charaInfo,
            SingleModeEventInfo[] uncheckedEvents,
            bool switchToWorkspace = true)
        {
            ParseSingleModeCheckEventResponse(charaInfo, uncheckedEvents, switchToWorkspace);
            return ValueTask.CompletedTask;
        }

        void ParseSingleModeCheckEventResponse(
            SingleModeChara charaInfo,
            SingleModeEventInfo[] uncheckedEvents,
            bool switchToWorkspace = true)
        {
            var events = ImmutableArray.CreateBuilder<EventDisplayRenderer.Event>(uncheckedEvents.Length);
            foreach (var i in uncheckedEvents)
                events.Add(Database.Events.TryGetValue(i.story_id, out var story)
                    ? BuildKnownEventTree(charaInfo, i, story)
                    : BuildUnknownEventTree(i));

            if (events.Count == 0)
                return;

            var snapshot = new EventDisplayRenderer.DisplaySnapshot(events.ToImmutable());
            lock (stateGate)
            {
                workspace ??= Workspace.Create(WorkspaceTitle);
                workspace.SetPanel(
                    EventsPanelKey,
                    "事件分析",
                    EventDisplayRenderer.Render(snapshot),
                    fullBleed: true,
                    switchToWorkspace: switchToWorkspace);
            }
        }

        static bool IsLegendBuffSelection(SingleModeChara charaInfo, SingleModeEventInfo[] uncheckedEvents)
            => charaInfo.playing_state == LegendBuffSelectionPlayingState
               && uncheckedEvents.Any(x => x.story_id == LegendBuffSelectionStoryId);

        static EventDisplayRenderer.Event BuildKnownEventTree(
            SingleModeChara charaInfo,
            SingleModeEventInfo eventInfo,
            Story story)
        {
            var options = ImmutableArray.CreateBuilder<EventDisplayRenderer.Option>(eventInfo.event_contents_info.choice_array.Length);
            for (var j = 0; j < eventInfo.event_contents_info.choice_array.Length; ++j)
            {
                var originalChoice = GetStoryChoice(story, eventInfo.event_contents_info.choice_array[j], j);

                var option = string.IsNullOrEmpty(originalChoice.Option)
                    ? I18N_NoOption
                    : originalChoice.Option;
                var successEffect = EnrichSkillEffect(originalChoice.SuccessEffect, charaInfo);
                var failedEffect = EnrichSkillEffect(originalChoice.FailedEffect, charaInfo);
                ImmutableArray<EventDisplayRenderer.Effect> effects = string.IsNullOrEmpty(originalChoice.FailedEffect)
                    ? [new(successEffect, EventDisplayRenderer.EffectKind.Normal)]
                    : originalChoice.SuccessEffect == I18N_UnknownEffect
                      && originalChoice.FailedEffect == I18N_UnknownEffect
                        ?
                        [
                            new(
                                EventDisplayRenderer.StyledText.Plain(I18N_UnknownEffect),
                                EventDisplayRenderer.EffectKind.Unknown)
                        ]
                        :
                        [
                            new(
                                successEffect.Prepend($"({I18N_WhenSuccess})"),
                                EventDisplayRenderer.EffectKind.Success),
                            new(
                                failedEffect.Prepend($"({I18N_WhenFail})"),
                                EventDisplayRenderer.EffectKind.Failure)
                        ];
                options.Add(new(option, effects));
            }

            return new(story.TriggerName, story.Name, eventInfo.story_id, options.ToImmutable());
        }

        static EventDisplayRenderer.Event BuildUnknownEventTree(SingleModeEventInfo eventInfo)
            => new(
                I18N_UnknownSource,
                I18N_UnknownEvent,
                eventInfo.story_id,
                [.. eventInfo.event_contents_info.choice_array.Select(choice =>
                    new EventDisplayRenderer.Option(
                        string.Format(I18N_UnknownOption, FirstSelectIndex(choice)),
                        [
                            new(
                                EventDisplayRenderer.StyledText.Plain(I18N_UnknownEffect),
                                EventDisplayRenderer.EffectKind.Normal)
                        ]))]);

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

        static EventDisplayRenderer.StyledText EnrichSkillEffect(
            string effect,
            SingleModeChara charaInfo)
        {
            var enriched = new StringBuilder(effect.Length);
            var skillLevels = ImmutableArray.CreateBuilder<EventDisplayRenderer.SkillLevelSpan>();
            var copiedUntil = 0;

            foreach (Match matchedSkillName in ExtractSkillNameRegex.Matches(effect))
            {
                var skillNameGroup = matchedSkillName.Groups[1];
                var skillName = skillNameGroup.Value;
                enriched.Append(effect.AsSpan(copiedUntil, skillNameGroup.Index - copiedUntil));
                if (!Database.Skills.TryFindByName(skillName, out var skill))
                {
                    enriched.Append(skillName);
                    copiedUntil = skillNameGroup.Index + skillNameGroup.Length;
                    continue;
                }

                var tip = charaInfo.skill_tips_array?.FirstOrDefault(x => x.group_id == skill.GroupId && x.rarity == skill.Rarity);
                var level = tip?.level ?? 0;
                var levelText = level.ToString();
                enriched.Append(skillName).Append("(Lv.");
                skillLevels.Add(new(enriched.Length, levelText.Length, level));
                enriched.Append(levelText).Append(')');
                copiedUntil = skillNameGroup.Index + skillNameGroup.Length;
            }

            enriched.Append(effect.AsSpan(copiedUntil));
            return new(enriched.ToString(), skillLevels.ToImmutable());
        }

        static int FirstSelectIndex(ChoiceArray choice)
        {
            return choice.select_index_info_array?.FirstOrDefault()?.select_index ?? 0;
        }
    }

}
