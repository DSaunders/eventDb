﻿using System.Collections.Generic;
using System.Threading.Tasks;
using EasyEvents.Core.ClientInterfaces;
using EasyEvents.Core.Configuration;
using EasyEvents.Core.Exceptions;
using EasyEvents.Core.Stores.InMemory;
using EasyEvents.Core.Tests.TestEvents;
using Shouldly;
using Xunit;

namespace EasyEvents.Core.Tests
{
    public class EventPublishingTests
    {
        private readonly SimpleTextEventHandler _simpleTextEventHandler;
        private readonly IEasyEvents _easyEvents;
        private readonly List<SimpleTextEvent> _eventList;

        public EventPublishingTests()
        {
            _eventList = new List<SimpleTextEvent>();
            _simpleTextEventHandler = new SimpleTextEventHandler(_eventList);

            _easyEvents = new EasyEvents();

            _easyEvents.Configure(new EasyEventsConfiguration
            {
                HandlerFactory = type =>
                {
                    return (type == typeof(IEventHandler<SimpleTextEvent>))
                    ? _simpleTextEventHandler
                    : null;

                },
                Store = new InMemoryEventStore()
            });

            DateAbstraction.Pause();

            // while (!System.Diagnostics.Debugger.IsAttached)
            //{
            //    System.Threading.Tasks.Task.Delay(100);
            //}
        }

        [Fact]
        public async Task Event_Calls_Handlers()
        {
            // Given
            var testEvent = new SimpleTextEvent("test");

            // When
            await _easyEvents.RaiseEventAsync(testEvent);

            // Then
            _eventList.Count.ShouldBe(1);
            _eventList[0].ShouldBe(testEvent);
        }

        [Fact]
        public async Task Replays_All_Events_In_Order()
        {
            // Given
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("event 1"));
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("event 2"));
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("event 3"));
            _eventList.Clear();

            // When
            await _easyEvents.ReplayAllEventsAsync();

            // Then
            _eventList[0].SomeTestValue.ShouldBe("event 1");
            _eventList[1].SomeTestValue.ShouldBe("event 2");
            _eventList[2].SomeTestValue.ShouldBe("event 3");
        }

        [Fact]
        public async Task Throws_If_HandlerFactory_Returns_Type_That_Does_Not_Implement_Handler_Interface()
        {
            // Given
            _easyEvents.Configure(new EasyEventsConfiguration
            {
                HandlerFactory = type => new object(),
                Store = new InMemoryEventStore()
            });

            // When
            var ex =
                await Record.ExceptionAsync(() => _easyEvents.RaiseEventAsync(new SimpleTextEvent("this should explode")));

            // Then
            ex.ShouldNotBeNull();
            ex.ShouldBeOfType<EventHandlerException>();
            ex.Message.ShouldBe(
                $"Cannot handle {nameof(SimpleTextEvent)}. Handler returned from Factory does not implement {nameof(IEventHandler<IEvent>)}<{nameof(SimpleTextEvent)}>");
        }

        [Fact]
        public async Task Throws_If_HandlerFactory_Returns_Handler_That_Does_Not_Handle_This_Event_Type()
        {
            // Given
            _easyEvents.Configure(new EasyEventsConfiguration
            {
                HandlerFactory = type => new NullEventHandler(),
                Store = new InMemoryEventStore()
            });

            // When
            var ex =
                await Record.ExceptionAsync(() => _easyEvents.RaiseEventAsync(new SimpleTextEvent("this should explode")));

            // Then
            ex.ShouldNotBeNull();
            ex.ShouldBeOfType<EventHandlerException>();
            ex.Message.ShouldBe(
                $"Cannot handle {nameof(SimpleTextEvent)}. Handler returned from Factory does not implement {nameof(IEventHandler<IEvent>)}<{nameof(SimpleTextEvent)}>");
        }

        [Fact]
        public async Task Does_Not_Throw_If_HandlerFactory_Returns_No_EventHandlers()
        {
            // Given
            _easyEvents.Configure(new EasyEventsConfiguration
            {
                HandlerFactory = type => null,
                Store = new InMemoryEventStore()
            });

            // When
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("this should NOT explode"));
        }


        [Fact]
        public async Task Handlers_Can_Raise_New_Events_During_Normal_Operation()
        {
            // Given
            var store = new TestEventStore();
            _easyEvents.Configure(new EasyEventsConfiguration
            {
                Store = store,
                HandlerFactory = type =>
                {
                    if (type == typeof(IEventHandler<RaisesAnotherEvent>))
                        return new RaisesAnotherEventHandler(_easyEvents);
                    return new NullEventHandler();
                }
            });

            // When
            await _easyEvents.RaiseEventAsync(new RaisesAnotherEvent());

            // Then
            store.Events[0].ShouldBeOfType<RaisesAnotherEvent>();
            store.Events[1].ShouldBeOfType<NullEvent>();
        }

        [Fact]
        public async Task Handles_Do_Not_Raise_Events_Again_When_Events_Are_Being_Replayed()
        {
            // Given
            var store = new TestEventStore();
            _easyEvents.Configure(new EasyEventsConfiguration
            {
                Store = store,
                HandlerFactory = type =>
                {
                    if (type == typeof(IEventHandler<RaisesAnotherEvent>))
                        return new RaisesAnotherEventHandler(_easyEvents);
                    return new NullEventHandler();
                }
            });
            await _easyEvents.RaiseEventAsync(new RaisesAnotherEvent());

            // When
            _easyEvents.ReplayAllEventsAsync().Wait();

            // Then
            store.Events.Count.ShouldBe(2);
            store.Events[0].ShouldBeOfType<RaisesAnotherEvent>();
            store.Events[1].ShouldBeOfType<NullEvent>();
        }


        [Fact]
        public async Task Runs_Processors_On_Event_Streams()
        {
            // Given
            _easyEvents.AddProcessorForStream("TestStream", async (s, e) =>
            {
                var typedEvent = e as SimpleTextEvent;

                if (typedEvent != null && typedEvent.SomeTestValue == "First event")
                    await _easyEvents.RaiseEventAsync(new SimpleTextEvent(typedEvent.SomeTestValue + " re-raised"));

            });

            // When
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("First event"));

            // Then
            _eventList.Count.ShouldBe(2);
            _eventList[1].SomeTestValue.ShouldBe("First event re-raised");
        }

        [Fact]
        public async Task Does_Not_Run_Processors_On_Incorrect_Stream()
        {
            // Given
            _easyEvents.AddProcessorForStream("SomeOtherStream", async (s, e) =>
            {
                await _easyEvents.RaiseEventAsync(new SimpleTextEvent("This shouldn't happen"));
            });

            // When
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("First event"));

            // Then
            _eventList.Count.ShouldBe(1);
        }

        [Fact]
        public async Task Allows_Processors_To_Store_State_For_A_Stream()
        {
            // Given
            _easyEvents.AddProcessorForStream("TestStream", async (s, e) =>
            {
                s["count"] = s.ContainsKey("count")
                    ? (int)s["count"] + 1
                    : 1;

                if ((int)s["count"] == 3)
                    await _easyEvents.RaiseEventAsync(new SimpleTextEvent("Third event fired"));
            });

            // When
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("Event 1"));
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("Event 2"));
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("Event 3"));
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("Event 4"));

            // Then
            _eventList.Count.ShouldBe(5);
            _eventList[0].SomeTestValue.ShouldBe("Event 1");
            _eventList[1].SomeTestValue.ShouldBe("Event 2");
            _eventList[2].SomeTestValue.ShouldBe("Event 3");
            _eventList[3].SomeTestValue.ShouldBe("Third event fired");
            _eventList[4].SomeTestValue.ShouldBe("Event 4");
        }


        [Fact]
        public async Task Runs_Processors_When_Replaying_Events()
        {
            // Given
            var log = new List<string>();
            _easyEvents.AddProcessorForStream("TestStream", (s, e) =>
            {
                log.Add("Processor Ran");
                return Task.CompletedTask;
            });

            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("Event"));

            // When
            await _easyEvents.ReplayAllEventsAsync();

            // Then
            log.Count.ShouldBe(2);
        }

        [Fact]
        public async Task Processors_Do_Not_Raise_Events_Again_When_Replaying_Events()
        {
            // Given
            var store = new TestEventStore();
            _easyEvents.Configure(new EasyEventsConfiguration
            {
                Store = store,
                HandlerFactory = type => new SimpleTextEventHandler(new List<SimpleTextEvent>())
            });

            _easyEvents.AddProcessorForStream("TestStream", async (s, e) =>
            {
                // Processor that raises another event
                await _easyEvents.RaiseEventAsync(new SimpleTextEvent("Raised by processor", "AnotherStream"));
            });

            // When
            await _easyEvents.RaiseEventAsync(new SimpleTextEvent("Raised by test"));

            // Then
            await _easyEvents.ReplayAllEventsAsync();

            store.Events.Count.ShouldBe(2);
            ((SimpleTextEvent)store.Events[0]).SomeTestValue.ShouldBe("Raised by test");
            ((SimpleTextEvent)store.Events[1]).SomeTestValue.ShouldBe("Raised by processor");
        }

        [Fact]
        public async Task Still_Runs_Processors_When_No_Handler_For_Event()
        {
            // Given
            var log = new List<string>();
            _easyEvents.AddProcessorForStream("TestStream", (s, e) =>
            {
                log.Add("Processor Ran");
                return Task.CompletedTask;
            });

            // When
            await _easyEvents.RaiseEventAsync(new NullEvent());

            // Then
            log.Count.ShouldBe(1);
        }


        [Fact]
        public async Task Populates_DateTime_Property_With_UTC_DateTime_If_Property_Exists()
        {
            // Given
            var testEvent = new HasDateTimePropertyEvent();

            // When
            await _easyEvents.RaiseEventAsync(testEvent);

            // Then
            testEvent.DateTime.ShouldBe(DateAbstraction.UtcNow);
        }

        [Fact]
        public async Task Does_Not_Populate_DateTime_When_Property_Is_Incorrect_Type()
        {
            // Given
            var testEvent = new HasDateTimePropertyWithIncorrectTypeEvent();

            // When
            await _easyEvents.RaiseEventAsync(testEvent);

            // Then
            testEvent.DateTime.ShouldBeNull();
        }

        [Fact]
        public async Task Does_Not_Populate_DateTime_When_Property_Has_No_Setter()
        {
            // Given
            var testEvent = new HasDateTimePropertyWithNoSetterEvent();

            // When
            await _easyEvents.RaiseEventAsync(testEvent);

            // Then
            testEvent.DateTime.ShouldBeNull();
        }
    }
}
