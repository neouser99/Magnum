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
namespace Magnum.Channels
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Extensions;
	using Fibers;
	using Logging;

	/// <summary>
	/// Publishes a messages to multiple channels
	/// </summary>
	/// <typeparam name="T">Channel type</typeparam>
	public class PublishSubscribeChannel<T> :
		Channel<T>
	{
		private static readonly ILogger _log = Logger.GetLogger<PublishSubscribeChannel<T>>();

		private readonly Fiber _fiber;
		private readonly Channel<T>[] _subscribers;

		public PublishSubscribeChannel(Fiber fiber, IEnumerable<Channel<T>> subscribers)
		{
			_fiber = fiber;
			_subscribers = subscribers.ToArray();
		}

		public Channel<T>[] Subscribers
		{
			get { return _subscribers; }
		}

		public void Send(T message)
		{
			_fiber.Enqueue(() =>
				{
					_subscribers.Each(subscriber =>
						{
							try
							{
								subscriber.Send(message);
							}
							catch (Exception ex)
							{
								_log.Error(ex, "Subscriber exception on Send");
							}
						});
				});
		}
	}
}