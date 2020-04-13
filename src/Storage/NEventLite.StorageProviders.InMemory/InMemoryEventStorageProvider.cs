﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NEventLite.Core.Domain;
using NEventLite.Exceptions;
using NEventLite.Storage;

namespace NEventLite.StorageProviders.InMemory
{
    public class InMemoryEventStorageProvider : InMemoryEventStorageProvider<Guid>, IEventStorageProvider
    {
        public InMemoryEventStorageProvider() : this(string.Empty)
        {
        }

        public InMemoryEventStorageProvider(string memoryDumpFile) : base(memoryDumpFile)
        {
        }
    }

    public class InMemoryEventStorageProvider<TEventKey> : IEventStorageProvider<TEventKey>
    {
        private readonly string _memoryDumpFile;
        private readonly Dictionary<string, List<object>> _eventStream =
            new Dictionary<string, List<object>>();

        public InMemoryEventStorageProvider() : this(string.Empty)
        {
        }

        public InMemoryEventStorageProvider(string memoryDumpFile)
        {
            _memoryDumpFile = memoryDumpFile;

            if (!string.IsNullOrWhiteSpace(_memoryDumpFile) && File.Exists(_memoryDumpFile))
            {
                _eventStream =
                    SerializerHelper.LoadFromFile(_memoryDumpFile) as
                        Dictionary<string, List<object>> ??
                    new Dictionary<string, List<object>>();
            }
        }

        public Task<IEnumerable<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>>> GetEventsAsync<TAggregate, TAggregateKey>(TAggregateKey aggregateId, int start, int count)
            where TAggregate : AggregateRoot<TAggregateKey, TEventKey>
        {
            IEnumerable<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>> result = null;

            if (_eventStream.ContainsKey(aggregateId.ToString()))
            {

                //this is needed for make sure it doesn't fail when we have int.maxValue for count
                if (count > int.MaxValue - start)
                {
                    count = int.MaxValue - start;
                }

                result = _eventStream[aggregateId.ToString()].Where(
                            o =>
                                (_eventStream[aggregateId.ToString()].IndexOf(o) >= start) &&
                                (_eventStream[aggregateId.ToString()].IndexOf(o) < (start + count)))
                        .Cast<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>>()
                        .ToArray();
            }
            else
            {
                result = new List<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>>();
            }

            return Task.FromResult(result);
        }

        public Task<IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>> GetLastEventAsync<TAggregate, TAggregateKey>(TAggregateKey aggregateId)
            where TAggregate : AggregateRoot<TAggregateKey, TEventKey>
        {
            if (_eventStream.ContainsKey(aggregateId.ToString()))
            {
                return Task.FromResult((IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>)_eventStream[aggregateId.ToString()].Last());
            }

            return Task.FromResult((IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey>)null);
        }

        public Task SaveAsync<TAggregate, TAggregateKey>(TAggregate aggregate)
            where TAggregate : AggregateRoot<TAggregateKey, TEventKey>
        {
            var events = aggregate.GetUncommittedChanges();

            if (events.Any())
            {
                if (_eventStream.ContainsKey(aggregate.Id.ToString()) == false)
                {
                    _eventStream.Add(aggregate.Id.ToString(), events.Cast<object>().ToList());
                }
                else
                {
                    _eventStream[aggregate.Id.ToString()].AddRange(events);
                }
            }

            if (!string.IsNullOrWhiteSpace(_memoryDumpFile))
            {
                SerializerHelper.SaveToFile(_memoryDumpFile, _eventStream);
            }

            return Task.CompletedTask;
        }
    }
}
