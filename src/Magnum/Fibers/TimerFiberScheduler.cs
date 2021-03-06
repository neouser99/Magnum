// Copyright 2007-2008 The Apache Software Foundation.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace Magnum.Fibers
{
	using System;
	using System.ComponentModel;
	using System.Diagnostics;
	using System.Threading;
	using Extensions;
	using Internal;
	using Logging;

	[DebuggerDisplay("{GetType().Name} ( Count: {Count}, Next: {NextActionTime} )")]
	public class TimerFiberScheduler :
		FiberScheduler
	{
		private static readonly ILogger _log = Logger.GetLogger<TimerFiberScheduler>();

		private readonly ScheduledActionList _actions = new ScheduledActionList();
		private readonly object _lock = new object();
		private readonly Func<DateTime> _now = () => SystemUtil.UtcNow;
		private readonly Fiber _fiber;
		private readonly TimeSpan _timerInterval = -1.Milliseconds();
		private bool _disabled;
		private Timer _timer;

		public TimerFiberScheduler(Fiber fiber)
		{
			_fiber = fiber;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected int Count
		{
			get { return _actions.Count; }
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected string NextActionTime
		{
			get
			{
				DateTime now = Now;

				DateTime scheduledAt;
				if (_actions.GetNextScheduledActionTime(now, out scheduledAt))
					return scheduledAt.ToString();

				return "None";
			}
		}

		private DateTime Now
		{
			get { return _now(); }
		}

		public ScheduledAction Schedule(int interval, Fiber fiber, Action action)
		{
			return Schedule(interval.Milliseconds(), fiber, action);
		}

		public ScheduledAction Schedule(TimeSpan interval, Fiber fiber, Action action)
		{
			var scheduled = new SingleScheduledAction(GetScheduledTime(interval), fiber, action);
			Schedule(scheduled);

			return scheduled;
		}

		public ScheduledAction Schedule(int interval, int periodicInterval, Fiber fiber, Action action)
		{
			return Schedule(interval.Milliseconds(), periodicInterval.Milliseconds(), fiber, action);
		}

		public ScheduledAction Schedule(TimeSpan interval, TimeSpan periodicInterval, Fiber fiber, Action action)
		{
			SingleScheduledAction scheduled = null;
			scheduled = new SingleScheduledAction(GetScheduledTime(interval), fiber, () =>
				{
					try
					{
						action();
					}
					catch (Exception ex)
					{
						_log.Error(ex);
					}
					finally
					{
						scheduled.ScheduledAt = GetScheduledTime(periodicInterval);
						Schedule(scheduled);
					}
				});
			Schedule(scheduled);

			return scheduled;
		}

		public void Disable()
		{
			_disabled = true;

			lock (_lock)
			{
				if (_timer != null)
				{
					_timer.Dispose();
				}
			}
		}

		public void Schedule(ExecuteScheduledAction action)
		{
			_fiber.Enqueue(() =>
				{
					_actions.Add(action);

					ScheduleTimer();
				});
		}

		private void ScheduleTimer()
		{
			DateTime now = Now;

			DateTime scheduledAt;
			if (_actions.GetNextScheduledActionTime(now, out scheduledAt))
			{
				lock (_lock)
				{
					TimeSpan dueTime = scheduledAt - now;

					if (_timer != null)
					{
						_timer.Change(dueTime, _timerInterval);
					}
					else
					{
						_timer = new Timer(x => _fiber.Enqueue(ExecuteExpiredActions), this, dueTime, _timerInterval);
					}
				}
			}
		}

		private void ExecuteExpiredActions()
		{
			if (_disabled)
				return;

			ExecuteScheduledAction[] expiredActions;
			while ((expiredActions = _actions.GetExpiredActions(Now)).Length > 0)
			{
				expiredActions.Each(action =>
					{
						try
						{
							action.Execute();
						}
						catch (Exception ex)
						{
							_log.Error(ex);
						}
					});
			}

			ScheduleTimer();
		}

		private DateTime GetScheduledTime(TimeSpan interval)
		{
			return Now + interval;
		}
	}
}