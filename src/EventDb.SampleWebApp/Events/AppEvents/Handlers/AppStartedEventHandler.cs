﻿namespace EventDb.SampleWebApp.Events.AppEvents.Handlers
{
    using System.Threading.Tasks;
    using EventDb.Core.ClientInterfaces;

    internal class AppStartedEventHandler : IEventHandler<AppStartedEvent>
    {
        public async Task HandleEvent(AppStartedEvent @event)
        {
            Logger.LogMessage("App Started");
        }
    }
}