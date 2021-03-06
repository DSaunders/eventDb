using EasyEvents.Core.ClientInterfaces;

namespace EasyEvents.SampleWebApp.Events.AppEvents
{
    /// <summary>
    /// This is the aggregate, and therefor defines the stream name.
    /// All events that relate to this aggregate derive from this, and will be on the same stream
    /// </summary>
    public class AppEvent : IEvent
    {
        public string Stream => "AppEvents";
    }
}