using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NEventLite.Exceptions;
using NEventLite.Util;

namespace NEventLite.Core.Domain
{
    public abstract class AggregateRoot : AggregateRoot<Guid, Guid>
    {
    }

    public abstract class AggregateRoot<TAggregateKey, TEventKey> : IAggregateRoot
    {
        public TAggregateKey Id { get; private set; }
        public long CurrentVersion { get; private set; }
        public long LastCommittedVersion { get; private set; }
        public StreamState StreamState => (CurrentVersion == -1) ? StreamState.NoStream : StreamState.HasStream;

        private readonly IList<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>> _uncommittedChanges = 
            new List<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>>();
        private readonly Dictionary<Type, string> _eventHandlerCache;
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _applyLock = new SemaphoreSlim(1, 1);

        protected AggregateRoot()
        {
            CurrentVersion = (long)StreamState.NoStream;
            LastCommittedVersion = (long)StreamState.NoStream;
            _eventHandlerCache = ReflectionHelper.FindEventHandlerMethodsInAggregate<TAggregateKey, TEventKey>(this.GetType());
        }

        public bool HasUncommittedChanges()
        {
            lock (_uncommittedChanges)
            {
                return _uncommittedChanges.Any();
            }
        }

        public ReadOnlyCollection<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>> GetUncommittedChanges()
        {
            lock (_uncommittedChanges)
            {
                return new ReadOnlyCollection<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>>(_uncommittedChanges);
            }
        }

        public void MarkChangesAsCommitted()
        {
            lock (_uncommittedChanges)
            {
                _uncommittedChanges.Clear();
                LastCommittedVersion = CurrentVersion;
            }
        }

        public async Task LoadsFromHistoryAsync(IEnumerable<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>> history)
        {
            await _loadLock.WaitAsync();

            try
            {
                foreach (var e in history)
                {
                    //We call ApplyEvent with isNew parameter set to false as we are replaying a historical event
                    await ApplyEventAsync(e, false);
                }

                LastCommittedVersion = CurrentVersion;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        protected void ApplyEvent<TAggregate>(IEvent<TAggregate, TAggregateKey, TEventKey> @event) where TAggregate: AggregateRoot<TAggregateKey, TEventKey>
        {
            ApplyEventAsync(@event).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        // This method is only used when applying new events
        protected async Task ApplyEventAsync<TAggregate>(IEvent<TAggregate, TAggregateKey, TEventKey> @event) where TAggregate : AggregateRoot<TAggregateKey, TEventKey>
        {
            await _loadLock.WaitAsync();

            try
            {
                await ApplyEventAsync(@event, true);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private async Task ApplyEventAsync(IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey> @event, bool isNew)
        {
            //All state changes to AggregateRoot must happen via the Apply method
            //Make sure the right Apply method is called with the right type.
            //We can you use dynamic objects or reflection for this.

            await _applyLock.WaitAsync();

            try
            {
                if (CanApply(@event))
                {
                    await DoApplyAsync(@event, isNew);

                    if (isNew)
                    {
                        lock (_uncommittedChanges)
                        {
                            _uncommittedChanges.Add(@event); //only add to the events collection if it's a new event
                        }
                    }
                }
                else
                {
                    throw new AggregateStateMismatchException(
                        $"The event target version is {@event.AggregateId}.(Version {@event.TargetVersion}) and " +
                        $"AggregateRoot version is {this.Id}.(Version {CurrentVersion})");
                }
            }
            finally
            {
                _applyLock.Release();
            }
        }

        private bool CanApply(IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey> @event)
        {
            //Check to see if event is applying against the right Aggregate and matches the target version
            return ((StreamState == StreamState.NoStream || Id.Equals(@event.AggregateId))
                    && (CurrentVersion == @event.TargetVersion));
        }

        private async Task DoApplyAsync(IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey> @event, bool isNew)
        {
            if (StreamState == StreamState.NoStream)
            {
                Id = @event.AggregateId; //This is only needed for the very first event as every other event CAN ONLY apply to matching ID
            }

            if (_eventHandlerCache.ContainsKey(@event.GetType()))
            {
                var replayStatus = isNew ? ReplayStatus.Regular : ReplayStatus.Replay;
                await @event.InvokeOnAggregateAsync(this, _eventHandlerCache[@event.GetType()], replayStatus);
            }
            else
            {
                throw new AggregateEventOnApplyMethodMissingException($"No event handler specified for {@event.GetType()} on {this.GetType()}");
            }

            CurrentVersion++;
        }

        internal void HydrateFromSnapshot<TSnapshotKey>(ISnapshot<TAggregateKey, TSnapshotKey> snapshot)
        {
            Id = snapshot.AggregateId;
            CurrentVersion = snapshot.Version;
            LastCommittedVersion = snapshot.Version;
        }
    }

    public interface IAggregateRoot
    {
    }
}
