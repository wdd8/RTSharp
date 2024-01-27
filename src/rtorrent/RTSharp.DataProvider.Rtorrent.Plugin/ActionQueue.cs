using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Avalonia;

using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Rtorrent.Plugin
{
	public class ActionQueue : DefaultActionQueue
	{
		public override StyledElement Display { get; }

		public List<ActionQueueAction> _actions = new();

		private Views.ActionQueue ActionQueueView;
		private ViewModels.ActionQueueViewModel ActionQueueVm;

		public ActionQueue(string PluginDisplayName, Guid InstanceId)
		{
			ActionQueueView = new Views.ActionQueue() {
				DataContext = ActionQueueVm = new ViewModels.ActionQueueViewModel() {
					DisplayName = $"{PluginDisplayName} ({InstanceId})"
				}
			};
			Display = ActionQueueView;
		}

		private string RenderActionQueue(IEnumerable<ActionQueueAction> Actions, StringBuilder Builder, int Identation = 0)
		{
			foreach (var action in Actions) {
				var running = action.State switch {
					ACTION_STATE.WAITING => "[waiting]",
					ACTION_STATE.RUNNING => "[running]",
					ACTION_STATE.CANCELLED => "[cancelled]",
					ACTION_STATE.FAILED => "[failed]",
					ACTION_STATE.DONE => "[done]",
					_ => throw new ArgumentOutOfRangeException()
				};
				Builder.AppendLine(new string(Enumerable.Repeat('\t', Identation).ToArray()) + " " + running + " " + action.Name);
				if (action.ProgressDone != 0) {
					Builder.AppendLine(new string(Enumerable.Repeat('\t', Identation).ToArray()) + $" {Math.Round(action.ProgressDone, 2)}% {action.ProgressString}");
				}

				if (action.ChildActions.Any()) {
					RenderActionQueue(action.ChildActions, Builder, Identation + 1);
				}
			}

			return Builder.ToString();
		}

		public override void RenderActionQueue(IEnumerable<ActionQueueAction> Actions)
		{
			ActionQueueVm.ActionQueueString = RenderActionQueue(Actions, new StringBuilder());
		}

		public override void ActionCreated(ActionQueueAction Action) => ActionQueueVm.ActionsInQueue++;
		public override void ActionRun(ActionQueueAction Action) { }
		public override void ActionCompleted(ActionQueueAction Action) => ActionQueueVm.ActionsInQueue--;
		public override void ActionErrored(ActionQueueAction Action)
		{
			ActionQueueVm.ErroredActions++;
			ActionQueueVm.ActionsInQueue--;
		}
		public override void ActionExpired(ActionQueueAction Action) { }
	}
}