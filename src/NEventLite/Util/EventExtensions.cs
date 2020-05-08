using System;
using System.Threading.Tasks;
using NEventLite.Core;
using NEventLite.Core.Domain;
using NEventLite.Exceptions;

namespace NEventLite.Util
{
    public static class EventExtensions
    {
        public static async Task InvokeOnAggregateAsync<TAggregateKey, TEventKey>(this IEvent<AggregateRoot<TAggregateKey, TEventKey>, TAggregateKey, TEventKey> @event,
            AggregateRoot<TAggregateKey, TEventKey> aggregate, string methodName, ReplayStatus replayStatus)
        {
            var method = ReflectionHelper.GetMethod(aggregate.GetType(), methodName, new Type[] { @event.GetType() }); //Find the right method
            object[] args;
            if (method == null)
            {
                method = ReflectionHelper.GetMethod(aggregate.GetType(), methodName, new Type[] { @event.GetType(), typeof(ReplayStatus) }); // handler with replay status
                if (method == null)
                {
                    throw new AggregateEventOnApplyMethodMissingException($"No event Apply method found on {aggregate.GetType()} for {@event.GetType()}");
                }
                args = new object[] {@event, replayStatus}; 
            }
            else
            {
                args = new object[] {@event};
            }
            
            var task = method.Invoke(aggregate, args); //invoke with the event handler

            if (task != null && task.GetType() == typeof(Task))
            {
                await (Task)task;
            }
        
        }
    }
}
