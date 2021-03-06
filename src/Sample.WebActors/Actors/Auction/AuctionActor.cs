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
namespace Sample.WebActors.Actors.Auction
{
	using System;
	using Magnum;
	using Magnum.Channels;
	using Magnum.Fibers;



	/// <summary>
	/// A model representation of an auction, of which each auction running in a system would have
	/// an instance of this class to manage the operations.
	/// 
	/// Some interesting points in the design so far.
	/// 
	/// Channels work, particularly for commands sent to the actor (Bid, Ask)
	/// 
	/// Responses are absolutely ugly, having to be part of the command message sent to the actor
	/// There has to be a better way to handle this
	/// 
	/// Current thought is the creation of a "Mailbox" concept, whereby any type of message can be
	/// sent to the mailbox, and the mailbox will ensure proper delivery of messages to the appropriate channels
	/// 
	/// At the same time, a form of reservation that could be created and packaged with a message (including something
	/// similar to a MessageId or TransactionId, wrapped inside a Future() object) would support an easier notion of 
	/// binding a continuation to the receive of a response message.
	/// 
	/// This mailbox concept could be wrapped around an actor (or part of the api itself) to allow typed sends to the actor without
	/// knowing the specific input channel at the time of send
	/// 
	/// An actor repository, or something similar to that, would provide location services or something to deal with the 
	/// connection of actors. For instance, if a search is performed to find auctions for boats, the response would include a 
	/// list of actors, one for each auction. It may be that the query is sent to an actor, and a list of actors is returned
	/// instead. Not sure here, but clearly a way to map a message/guid to an instance is important.
	/// 
	/// It may just be the KeyedChannel that I created, whereas a message is sent to a keyed channel, which then 
	/// delivers it to the actor method itself.
	/// </summary>

	public class AuctionActor
	{
		private readonly decimal _openingBid;
		private readonly FiberScheduler _scheduler;
		private Channel<AuctionCompleted> _bidderCompleted;
		private Channel<Outbid> _bidderOutbid;
		private decimal _bidIncrement;
		private DateTime _expiresAt;
		private Fiber _fiber;
		private decimal _highestBid;

		public AuctionActor(Fiber fiber, FiberScheduler scheduler, DateTime expiresAt, decimal openingBid, decimal bidIncrement)
		{
			_fiber = fiber;
			_scheduler = scheduler;
			_expiresAt = expiresAt;
			_openingBid = openingBid;
			_bidIncrement = bidIncrement;
			_highestBid = openingBid - bidIncrement;

			BidChannel = new ConsumerChannel<Bid>(_fiber, ReceiveBid);
			AskChannel = new ConsumerChannel<Ask>(_fiber, ReceiveAsk);

			scheduler.Schedule(expiresAt.ToUniversalTime() - SystemUtil.UtcNow, _fiber, EndAuction);
		}

		public Channel<Ask> AskChannel { get; set; }
		public Channel<Bid> BidChannel { get; set; }

		private void ReceiveAsk(Ask message)
		{
			message.Status.Send(new Status
				{
					ExpiresAt = _expiresAt,
					Asked = _highestBid,
				});
		}

		private void EndAuction()
		{
			if (_highestBid >= _openingBid)
			{
				var completed = new AuctionCompleted
					{
						HighestBid = _highestBid
					};

				_bidderCompleted.Send(completed);
			}
		}


		private void ReceiveBid(Bid bid)
		{
			if (SystemUtil.UtcNow >= _expiresAt)
			{
				bid.Over.Send(new AuctionOver());
				return;
			}

			if (bid.Amount < _highestBid + _bidIncrement)
			{
				bid.Outbid.Send(new Outbid
					{
						Asked = _highestBid
					});

				return;
			}

			if (_highestBid >= _openingBid)
			{
				_bidderOutbid.Send(new Outbid
					{
						Asked = bid.Amount,
					});
			}

			_highestBid = bid.Amount;
			_bidderOutbid = bid.Outbid;
			_bidderCompleted = bid.Completed;
		}
	}

	public abstract class AuctionMessage
	{
	}

	public class Bid :
		AuctionMessage
	{
		public decimal Amount { get; set; }

		public Channel<Outbid> Outbid { get; set; }
		public Channel<AuctionOver> Over { get; set; }
		public Channel<AuctionCompleted> Completed { get; set; }
	}

	public class Ask :
		AuctionMessage
	{
		public Channel<Status> Status { get; set; }
	}

	public abstract class AuctionResponse
	{
	}

	public class Status :
		AuctionResponse
	{
		public decimal Asked { get; set; }
		public DateTime ExpiresAt { get; set; }
	}

	public class Accepted :
		AuctionResponse
	{
	}

	public class Outbid :
		AuctionResponse
	{
		public decimal Asked { get; set; }
	}

	public class AuctionCompleted :
		AuctionResponse
	{
		public decimal HighestBid { get; set; }
	}

	public class AuctionFailed :
		AuctionResponse
	{
	}

	public class AuctionOver :
		AuctionResponse
	{
	}
}