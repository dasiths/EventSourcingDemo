﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NEventLite.Core;
using NEventLite.Repository;
using NEventLite.Samples.ConsoleApp.Domain;
using NEventLite.Samples.ConsoleApp.Domain.Snapshot;
using NEventLite.Samples.ConsoleApp.Infrastructure;
using NEventLite.Storage;
using NEventLite.StorageProviders.InMemory;

namespace NEventLite.Samples.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            RunAsync().Wait();
            Console.ReadLine();
        }

        private static async Task RunAsync()
        {
            //This path is used to save in memory storage
            string strTempDataFolderPath = AppDomain.CurrentDomain.BaseDirectory + @"App_Data\";

            //create temp directory if it doesn't exist
            new FileInfo(strTempDataFolderPath).Directory.Create();

            var inMemoryEventStorePath = $@"{strTempDataFolderPath}events.stream.dump";
            var inMemorySnapshotStorePath = $@"{strTempDataFolderPath}events.snapshot.dump";
            var inMemoryReadModelStorePath = $@"{strTempDataFolderPath}events.readmodel.dump";

            File.Delete(inMemoryEventStorePath);
            File.Delete(inMemorySnapshotStorePath);
            File.Delete(inMemoryReadModelStorePath);

            IEventStorageProvider<Guid, Guid> eventStorage = 
                new InMemoryEventStorageProvider<Guid, Guid>(inMemoryEventStorePath);
            ISnapshotStorageProvider<Guid, Guid, ScheduleSnapshot> snapshotStorage = 
                new InMemorySnapshotStorageProvider<Guid, Guid, ScheduleSnapshot>(2, inMemorySnapshotStorePath);
            IClock clock = new MyClock();
            IEventPublisher<Guid, Guid> eventPublisher = new EventPublisher<Guid, Guid>();
            IRepository<Schedule, Guid, Guid> repository = 
                new Repository<Schedule, Guid, Guid, Guid, ScheduleSnapshot>(eventStorage, clock, snapshotStorage, eventPublisher);

            var schedule = new Schedule("test schedule");
            await repository.SaveAsync(schedule);

            schedule = await repository.GetByIdAsync(schedule.Id);
            schedule.AddTodo("test todo 1");
            await repository.SaveAsync(schedule);

            schedule = await repository.GetByIdAsync(schedule.Id);
            schedule.AddTodo("test todo 2");
            await repository.SaveAsync(schedule);

            schedule = await repository.GetByIdAsync(schedule.Id);
            schedule.AddTodo("test todo 3");
            await repository.SaveAsync(schedule);

            schedule = await repository.GetByIdAsync(schedule.Id);
            var todo = schedule.Todos.First();
            schedule.UpdateTodo(todo.Id, todo.Text + " updated");
            await repository.SaveAsync(schedule);

            schedule = await repository.GetByIdAsync(schedule.Id);
            todo = schedule.Todos.Last();
            await schedule.CompleteTodoAsync(todo.Id);
            await repository.SaveAsync(schedule);

            Console.WriteLine(JsonConvert.SerializeObject(schedule, Formatting.Indented));
        }
    }
}
