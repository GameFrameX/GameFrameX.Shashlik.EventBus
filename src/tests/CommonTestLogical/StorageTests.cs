﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using CommonTestLogical.EfCore;
using CommonTestLogical.TestEvents;
using GameFrameX.EventBus;
using GameFrameX.EventBus.RelationDbStorage;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shashlik.EventBus;
using Shashlik.Kernel.Dependency;
using Shashlik.Utils.Extensions;
using Shashlik.Utils.Helpers;
using Shouldly;

namespace CommonTestLogical
{
    /// <summary>
    /// 存储相关的测试逻辑
    /// </summary>
    [Transient]
    public class StorageTests
    {
        public StorageTests(IMessageStorage messageStorage, IOptions<EventBusOptions> eventBusOptions,
            IServiceProvider serviceProvider)
        {
            MessageStorage = messageStorage;
            EventBusOptions = eventBusOptions.Value;
            DbContext = serviceProvider.GetService<DemoDbContext>();
        }

        private EventBusOptions EventBusOptions { get; }
        private DemoDbContext DbContext { get; }
        private IMessageStorage MessageStorage { get; }

        public async Task SavePublishedNoTransactionTest()
        {
            // 正常的已发布消息写入，查询测试, 不带事务
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = null,
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Scheduled,
                IsLocking = false,
                LockEnd = null
            };
            var id = await MessageStorage.SavePublishedAsync(msg, null, default);
            msg.Id.ShouldBe(id);

            var dbMsg = await MessageStorage.FindPublishedByMsgIdAsync(msg.MsgId, default);
            dbMsg!.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);
            dbMsg.Status.ShouldBe(msg.Status);
        }

        public async Task SavePublishedWithTransactionCommitTest()
        {
            await using var tran = await DbContext.Database.BeginTransactionAsync();
            DbContext.Add(new Users { Name = "张三" });
            await DbContext.SaveChangesAsync();

            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = null,
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Scheduled,
                IsLocking = false,
                LockEnd = null
            };

            var transactionContext = DbContext.GetTransactionContext();
            var id = await MessageStorage.SavePublishedAsync(msg, transactionContext, default);
            await DbContext.SaveChangesAsync();

            var begin = DateTimeOffset.Now;
            while ((DateTimeOffset.Now - begin).TotalSeconds < 20)
            {
                // 20秒内不提交事务， 消息就应该是未提交
                (await MessageStorage.IsCommittedAsync(msg.MsgId, default)).ShouldBeFalse();
                await Task.Delay(300);
            }

            await tran.CommitAsync();
            (await MessageStorage.IsCommittedAsync(msg.MsgId, default)).ShouldBeTrue();

            msg.Id.ShouldBe(id);
            var dbMsg = await MessageStorage.FindPublishedByMsgIdAsync(msg.MsgId, default);
            dbMsg!.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);
            dbMsg.Status.ShouldBe(msg.Status);
        }

        public async Task SavePublishedWithTransactionRollBackTest()
        {
            await using var tran = await DbContext.Database.BeginTransactionAsync();
            DbContext.Add(new Users { Name = "张三" });
            await DbContext.SaveChangesAsync();
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = null,
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Scheduled,
                IsLocking = false,
                LockEnd = null
            };
            var transactionContext = new RelationDbStorageTransactionContext(tran.GetDbTransaction());
            var id = await MessageStorage.SavePublishedAsync(msg, transactionContext, default);

            var begin = DateTimeOffset.Now;
            while ((DateTimeOffset.Now - begin).TotalSeconds < 20)
            {
                // 20秒内不提交事务， 消息就应该是未提交
                (await MessageStorage.IsCommittedAsync(msg.MsgId, default)).ShouldBeFalse();
                await Task.Delay(300);
            }

            await tran.RollbackAsync();
            (await MessageStorage.IsCommittedAsync(msg.MsgId, default)).ShouldBeFalse();

            var dbMsg = await MessageStorage.FindPublishedByMsgIdAsync(msg.MsgId, default);
            dbMsg.ShouldBeNull();
        }

        public async Task SavePublishedWithTransactionDisposeTest()
        {
            var tran = await DbContext.Database.BeginTransactionAsync();
            DbContext.Add(new Users { Name = "张三" });
            await DbContext.SaveChangesAsync();
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = null,
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Scheduled,
                IsLocking = false,
                LockEnd = null
            };
            var transactionContext = new RelationDbStorageTransactionContext(tran.GetDbTransaction());
            var id = await MessageStorage.SavePublishedAsync(msg, transactionContext, default);

            var begin = DateTimeOffset.Now;
            while ((DateTimeOffset.Now - begin).TotalSeconds < 20)
            {
                // 20秒内不提交事务， 消息就应该是未提交
                (await MessageStorage.IsCommittedAsync(msg.MsgId, default)).ShouldBeFalse();
                await Task.Delay(300);
            }

            await tran.DisposeAsync();
            (await MessageStorage.IsCommittedAsync(msg.MsgId, default)).ShouldBeFalse();

            var dbMsg = await MessageStorage.FindPublishedByMsgIdAsync(msg.MsgId, default);
            dbMsg.ShouldBeNull();
        }

        public async Task SaveReceivedTest()
        {
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = null,
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Scheduled,
                IsLocking = false,
                LockEnd = null
            };
            var id = await MessageStorage.SaveReceivedAsync(msg, default);
            msg.Id.ShouldBe(id);

            (await MessageStorage.FindReceivedByMsgIdAsync(msg.MsgId, new EventHandlerDescriptor
                    {
                        EventHandlerName = "TestEventHandlerName1"
                    }, default))!.Id.ShouldBe(id);
        }

        public async Task TryLockPublishedTests()
        {
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = null,
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Scheduled,
                IsLocking = false,
                LockEnd = null
            };
            var id = await MessageStorage.SavePublishedAsync(msg, null, default);
            msg.Id.ShouldBe(id);

            // 锁5秒
            (await MessageStorage.TryLockPublishedAsync(id, DateTimeOffset.Now.AddSeconds(5), default)).ShouldBeTrue();
            // 再锁失败
            (await MessageStorage.TryLockPublishedAsync(id, DateTimeOffset.Now.AddSeconds(5), default)).ShouldBeFalse();

            // 6秒后再锁成功
            await Task.Delay(6000);
            (await MessageStorage.TryLockPublishedAsync(id, DateTimeOffset.Now.AddSeconds(6), default)).ShouldBeTrue();
        }

        public async Task TryLockReceivedTests()
        {
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = null,
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Scheduled,
                IsLocking = false,
                LockEnd = null
            };
            var id = await MessageStorage.SaveReceivedAsync(msg, default);
            msg.Id.ShouldBe(id);

            // 锁5秒
            (await MessageStorage.TryLockReceivedAsync(id, DateTimeOffset.Now.AddSeconds(5), default)).ShouldBeTrue();
            // 再锁失败
            (await MessageStorage.TryLockReceivedAsync(id, DateTimeOffset.Now.AddSeconds(5), default)).ShouldBeFalse();

            // 6秒后再锁成功
            await Task.Delay(6000);
            (await MessageStorage.TryLockReceivedAsync(id, DateTimeOffset.Now.AddSeconds(6), default)).ShouldBeTrue();
        }

        public async Task UpdatePublishedTests()
        {
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = null,
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Scheduled,
                IsLocking = false,
                LockEnd = null
            };
            var id = await MessageStorage.SavePublishedAsync(msg, null, default);
            msg.Id.ShouldBe(id);

            var expireAt = DateTimeOffset.Now.AddHours(1);
            await MessageStorage.UpdatePublishedAsync(id, MessageStatus.Succeeded, 1, DateTimeOffset.Now.AddHours(1),
                                                      default);

            var dbMsg = await MessageStorage.FindPublishedByMsgIdAsync(msg.MsgId, default);
            dbMsg!.RetryCount.ShouldBe(1);
            dbMsg!.Status.ShouldBe(MessageStatus.Succeeded);
            dbMsg.ExpireTime!.Value.GetLongDate().ShouldBe(expireAt.GetLongDate());
        }

        public async Task UpdateReceivedTests()
        {
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = null,
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Scheduled,
                IsLocking = false,
                LockEnd = null
            };
            var id = await MessageStorage.SaveReceivedAsync(msg, default);
            msg.Id.ShouldBe(id);

            var expireAt = DateTimeOffset.Now.AddHours(1);
            await MessageStorage.UpdateReceivedAsync(id, MessageStatus.Succeeded, 1, DateTimeOffset.Now.AddHours(1),
                                                     default);

            var dbMsg = await MessageStorage.FindReceivedByMsgIdAsync(msg.MsgId, new EventHandlerDescriptor
            {
                EventHandlerName = msg.EventHandlerName
            }, default);
            dbMsg!.RetryCount.ShouldBe(1);
            dbMsg!.Status.ShouldBe(MessageStatus.Succeeded);
            dbMsg.ExpireTime!.Value.GetLongDate().ShouldBe(expireAt.GetLongDate());
        }

        public async Task DeleteExpiresTests()
        {
            var @event = new TestEvent { Name = "张三" };
            Func<DateTimeOffset, MessageStatus, bool, string> addMsg = (expire, status, isReceive) =>
            {
                var model = new MessageStorageModel
                {
                    MsgId = Guid.NewGuid().ToString("n"),
                    Environment = EventBusOptions.Environment,
                    CreateTime = DateTimeOffset.Now,
                    DelayAt = null,
                    ExpireTime = expire,
                    EventHandlerName = "TestEventHandlerName1",
                    EventName = "TestEventName1",
                    EventBody = @event.ToJson(),
                    EventItems = "{}",
                    RetryCount = 0,
                    Status = status,
                    IsLocking = false,
                    LockEnd = null
                };

                if (isReceive)
                    MessageStorage.SaveReceivedAsync(model, default).GetAwaiter().GetResult();
                else
                    MessageStorage.SavePublishedAsync(model, null, default).GetAwaiter().GetResult();

                return model.Id;
            };


            var msg1 = addMsg(DateTimeOffset.Now.AddHours(-EventBusOptions.SucceedExpireHour - 1), MessageStatus.Failed,
                              false);
            var msg2 = addMsg(DateTimeOffset.Now.AddHours(-EventBusOptions.SucceedExpireHour - 1),
                              MessageStatus.Scheduled, false);
            var msg3 = addMsg(DateTimeOffset.Now.AddHours(-EventBusOptions.SucceedExpireHour - 1),
                              MessageStatus.Succeeded, false);
            var msg4 = addMsg(DateTimeOffset.Now.AddHours(1), MessageStatus.Succeeded, false);
            var msg5 = addMsg(DateTimeOffset.Now, MessageStatus.Failed, false);

            var msg6 = addMsg(DateTimeOffset.Now.AddHours(-EventBusOptions.SucceedExpireHour - 1), MessageStatus.Failed,
                              true);
            var msg7 = addMsg(DateTimeOffset.Now.AddHours(-EventBusOptions.SucceedExpireHour - 1),
                              MessageStatus.Scheduled, true);
            var msg8 = addMsg(DateTimeOffset.Now.AddHours(-EventBusOptions.SucceedExpireHour - 1),
                              MessageStatus.Succeeded, true);
            var msg9 = addMsg(DateTimeOffset.Now.AddHours(1), MessageStatus.Succeeded, true);
            var msg10 = addMsg(DateTimeOffset.Now.AddHours(1), MessageStatus.Failed, true);

            await MessageStorage.DeleteExpiresAsync(default);

            (await MessageStorage.FindPublishedByIdAsync(msg1, default)).ShouldNotBeNull();
            (await MessageStorage.FindPublishedByIdAsync(msg2, default)).ShouldNotBeNull();
            (await MessageStorage.FindPublishedByIdAsync(msg3, default)).ShouldBeNull();
            (await MessageStorage.FindPublishedByIdAsync(msg4, default)).ShouldNotBeNull();
            (await MessageStorage.FindPublishedByIdAsync(msg5, default)).ShouldNotBeNull();

            (await MessageStorage.FindReceivedByIdAsync(msg6, default)).ShouldNotBeNull();
            (await MessageStorage.FindReceivedByIdAsync(msg7, default)).ShouldNotBeNull();
            (await MessageStorage.FindReceivedByIdAsync(msg8, default)).ShouldBeNull();
            (await MessageStorage.FindReceivedByIdAsync(msg9, default)).ShouldNotBeNull();
            (await MessageStorage.FindReceivedByIdAsync(msg10, default)).ShouldNotBeNull();
        }

        public async Task GetPublishedMessagesOfNeedRetryAndLockTests()
        {
            var @event = new TestEvent { Name = "张三" };

            Func<DateTimeOffset, MessageStatus, string> addMsg = (createTime, status) =>
            {
                var model = new MessageStorageModel
                {
                    MsgId = Guid.NewGuid().ToString("n"),
                    Environment = EventBusOptions.Environment,
                    CreateTime = createTime,
                    DelayAt = null,
                    ExpireTime = null,
                    EventHandlerName = "TestEventHandlerName1",
                    EventName = "TestEventName1",
                    EventBody = @event.ToJson(),
                    EventItems = "{}",
                    RetryCount = 0,
                    Status = status,
                    IsLocking = false,
                    LockEnd = null
                };

                MessageStorage.SavePublishedAsync(model, null, default).GetAwaiter().GetResult();
                return model.Id;
            };

            var msg1 = addMsg(DateTimeOffset.Now.AddSeconds(-this.EventBusOptions.StartRetryAfter).AddSeconds(-10),
                              MessageStatus.Scheduled);
            var msg2 = addMsg(DateTimeOffset.Now.AddSeconds(-this.EventBusOptions.StartRetryAfter).AddSeconds(-10),
                              MessageStatus.Failed);
            var msg3 = addMsg(DateTimeOffset.Now.AddSeconds(-this.EventBusOptions.StartRetryAfter).AddSeconds(-10),
                              MessageStatus.Succeeded);
            var msg4 = addMsg(DateTimeOffset.Now, MessageStatus.Failed);

            // 正常数据操作测试
            {
                var list1 = await MessageStorage.GetPublishedMessagesOfNeedRetryAsync(100,
                                                                                      this.EventBusOptions.StartRetryAfter, this
                                                                                                                            .EventBusOptions
                                                                                                                            .RetryFailedMax, this.EventBusOptions.Environment, default);
                list1.Any(r => r.Id == msg1).ShouldBeTrue();
                list1.Any(r => r.Id == msg2).ShouldBeTrue();
                list1.Any(r => r.Id == msg3).ShouldBeFalse();
                list1.Any(r => r.Id == msg4).ShouldBeFalse();
            }
        }

        public async Task GetReceivedMessagesOfNeedRetryTests()
        {
            var @event = new TestEvent { Name = "张三" };

            Func<DateTimeOffset, MessageStatus, string> addMsg = (createTime, status) =>
            {
                var model = new MessageStorageModel
                {
                    MsgId = Guid.NewGuid().ToString("n"),
                    Environment = EventBusOptions.Environment,
                    CreateTime = createTime,
                    DelayAt = null,
                    ExpireTime = null,
                    EventHandlerName = "TestEventHandlerName1",
                    EventName = "TestEventName1",
                    EventBody = @event.ToJson(),
                    EventItems = "{}",
                    RetryCount = 0,
                    Status = status,
                    IsLocking = false,
                    LockEnd = null
                };

                MessageStorage.SaveReceivedAsync(model, default).GetAwaiter().GetResult();
                return model.Id;
            };

            var msg1 = addMsg(DateTimeOffset.Now.AddSeconds(-this.EventBusOptions.StartRetryAfter).AddSeconds(-10),
                              MessageStatus.Scheduled);
            var msg2 = addMsg(DateTimeOffset.Now.AddSeconds(-this.EventBusOptions.StartRetryAfter).AddSeconds(-10),
                              MessageStatus.Failed);
            var msg3 = addMsg(DateTimeOffset.Now.AddSeconds(-this.EventBusOptions.StartRetryAfter).AddSeconds(-10),
                              MessageStatus.Succeeded);
            var msg4 = addMsg(DateTimeOffset.Now, MessageStatus.Failed);

            // 正常数据操作测试
            {
                var list1 = await MessageStorage.GetReceivedMessagesOfNeedRetryAsync(
                                EventBusOptions.RetryLimitCount,
                                EventBusOptions.StartRetryAfter,
                                EventBusOptions.RetryFailedMax,
                                EventBusOptions.Environment,
                                default);
                list1.Any(r => r.Id == msg1).ShouldBeTrue();
                list1.Any(r => r.Id == msg2).ShouldBeTrue();
                list1.Any(r => r.Id == msg3).ShouldBeFalse();
                list1.Any(r => r.Id == msg4).ShouldBeFalse();
            }
        }

        public async Task QueryPublishedTests()
        {
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = DateTimeOffset.Now.AddHours(-1),
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Succeeded,
                IsLocking = false,
                LockEnd = null
            };

            var id = await MessageStorage.SavePublishedAsync(msg, null, default);
            var dbMsg = await MessageStorage.FindPublishedByIdAsync(id, default);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            var list = await MessageStorage.SearchPublishedAsync(msg.EventName, msg.Status, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchPublishedAsync(string.Empty, msg.Status, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchPublishedAsync(string.Empty, MessageStatus.None, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchPublishedAsync(msg.EventName, MessageStatus.None, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);
        }

        public async Task QueryReceivedTests()
        {
            var @event = new TestEvent { Name = "张三" };
            var msg = new MessageStorageModel
            {
                MsgId = Guid.NewGuid().ToString("n"),
                Environment = EventBusOptions.Environment,
                CreateTime = DateTimeOffset.Now,
                DelayAt = null,
                ExpireTime = DateTimeOffset.Now.AddHours(-1),
                EventHandlerName = "TestEventHandlerName1",
                EventName = "TestEventName1",
                EventBody = @event.ToJson(),
                EventItems = "{}",
                RetryCount = 0,
                Status = MessageStatus.Succeeded,
                IsLocking = false,
                LockEnd = null
            };

            var id = await MessageStorage.SaveReceivedAsync(msg, default);
            var dbMsg = await MessageStorage.FindReceivedByIdAsync(id, default);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            var list = await MessageStorage.SearchReceivedAsync(msg.EventName, msg.EventHandlerName, msg.Status, 0, 100,
                                                                default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchReceivedAsync(msg.EventName, msg.EventHandlerName, MessageStatus.None, 0, 100,
                                                            default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchReceivedAsync(msg.EventName, string.Empty, MessageStatus.None, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchReceivedAsync(msg.EventName, string.Empty, msg.Status, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);


            list = await MessageStorage.SearchReceivedAsync(string.Empty, msg.EventHandlerName, msg.Status, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchReceivedAsync(string.Empty, msg.EventHandlerName, MessageStatus.None, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchReceivedAsync(msg.EventName, msg.EventHandlerName, MessageStatus.None, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchReceivedAsync(string.Empty, msg.EventHandlerName, msg.Status, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchReceivedAsync(string.Empty, string.Empty, msg.Status, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);

            list = await MessageStorage.SearchReceivedAsync(msg.EventName, string.Empty, msg.Status, 0, 100, default);
            dbMsg = list.FirstOrDefault(r => r.Id == id);
            dbMsg.ShouldNotBeNull();
            dbMsg.Id.ShouldBe(id);
            dbMsg.EventName.ShouldBe(msg.EventName);
        }

        public void RelationDbStorageTransactionContextCommitTest()
        {
            using var tran = DbContext.Database.BeginTransaction();
            var transactionContext = DbContext.GetTransactionContext();
            transactionContext!.IsDone().ShouldBeFalse();
            tran.Commit();
            transactionContext!.IsDone().ShouldBeTrue();
        }

        public void RelationDbStorageTransactionContextRollbackTest()
        {
            using var tran = DbContext.Database.BeginTransaction();
            var transactionContext = DbContext.GetTransactionContext();
            transactionContext!.IsDone().ShouldBeFalse();
            tran.Rollback();
            transactionContext!.IsDone().ShouldBeTrue();
        }

        public void RelationDbStorageTransactionContextDisposeTest()
        {
            var tran = DbContext.Database.BeginTransaction();
            var transactionContext = DbContext.GetTransactionContext();
            transactionContext!.IsDone().ShouldBeFalse();
            tran.Dispose();
            transactionContext!.IsDone().ShouldBeTrue();
        }

        public void XaTransactionContextCommitTest()
        {
            var tran = new TransactionScope();
            var transactionContext = new XaTransactionContext(Transaction.Current!);
            transactionContext!.IsDone().ShouldBeFalse();
            tran.Complete();
            tran.Dispose();
            transactionContext!.IsDone().ShouldBeTrue();
        }

        public void XaTransactionContextRollbackTest()
        {
            var tran = new TransactionScope();
            var transactionContext = new XaTransactionContext(Transaction.Current!);
            transactionContext!.IsDone().ShouldBeFalse();
            Transaction.Current.Rollback();
            tran.Dispose();
            transactionContext!.IsDone().ShouldBeTrue();
        }

        public void XaTransactionContextDisposeTest()
        {
            var tran = new TransactionScope();
            var transactionContext = new XaTransactionContext(Transaction.Current!);
            transactionContext!.IsDone().ShouldBeFalse();
            tran.Dispose();
            transactionContext!.IsDone().ShouldBeTrue();
        }

        public async Task GetPublishedMessageStatusCountsTest()
        {
            var @event = new TestEvent { Name = "张三" };

            var count = RandomHelper.Next(3, 20);
            int success = 0, failed = 0, scheduled = 0;
            for (int i = 0; i < count; i++)
            {
                var status = (_Status)(i % 3);
                switch (status)
                {
                    case _Status.SUCCEEDED:
                        success++;
                        break;
                    case _Status.FAILED:
                        failed++;
                        break;
                    case _Status.SCHEDULED:
                        scheduled++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                var msg = new MessageStorageModel
                {
                    MsgId = Guid.NewGuid().ToString("n"),
                    Environment = EventBusOptions.Environment,
                    CreateTime = DateTimeOffset.Now,
                    DelayAt = null,
                    ExpireTime = DateTimeOffset.Now.AddHours(-1),
                    EventHandlerName = "TestEventHandlerName1",
                    EventName = "TestEventName1",
                    EventBody = @event.ToJson(),
                    EventItems = "{}",
                    RetryCount = 0,
                    Status = (MessageStatus)status,
                    IsLocking = false,
                    LockEnd = null
                };

                await MessageStorage.SavePublishedAsync(msg, null, default);
            }

            var publishedMessageStatusCountsAsync = await MessageStorage.GetPublishedMessageStatusCountsAsync(default);
            publishedMessageStatusCountsAsync[(MessageStatus)_Status.SUCCEEDED].ShouldBeGreaterThanOrEqualTo(success);
            publishedMessageStatusCountsAsync[(MessageStatus)_Status.FAILED].ShouldBeGreaterThanOrEqualTo(failed);
            publishedMessageStatusCountsAsync[(MessageStatus)_Status.SCHEDULED].ShouldBeGreaterThanOrEqualTo(scheduled);
        }

        public async Task GetReceivedMessageStatusCountsTest()
        {
            var @event = new TestEvent { Name = "张三" };

            var count = RandomHelper.Next(3, 20);
            int success = 0, failed = 0, scheduled = 0;

            for (int i = 0; i < count; i++)
            {
                var status = (_Status)(i % 3);
                switch (status)
                {
                    case _Status.SUCCEEDED:
                        success++;
                        break;
                    case _Status.FAILED:
                        failed++;
                        break;
                    case _Status.SCHEDULED:
                        scheduled++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                var msg = new MessageStorageModel
                {
                    MsgId = Guid.NewGuid().ToString("n"),
                    Environment = EventBusOptions.Environment,
                    CreateTime = DateTimeOffset.Now,
                    DelayAt = null,
                    ExpireTime = DateTimeOffset.Now.AddHours(-1),
                    EventHandlerName = "TestEventHandlerName1",
                    EventName = "TestEventName1",
                    EventBody = @event.ToJson(),
                    EventItems = "{}",
                    RetryCount = 0,
                    Status = (MessageStatus)status,
                    IsLocking = false,
                    LockEnd = null
                };

                await MessageStorage.SaveReceivedAsync(msg, default);
            }

            var receivedMessageStatusCountAsync = await MessageStorage.GetReceivedMessageStatusCountAsync(default);
            receivedMessageStatusCountAsync[(MessageStatus)_Status.SUCCEEDED].ShouldBeGreaterThanOrEqualTo(success);
            receivedMessageStatusCountAsync[(MessageStatus)_Status.FAILED].ShouldBeGreaterThanOrEqualTo(failed);
            receivedMessageStatusCountAsync[(MessageStatus)_Status.SCHEDULED].ShouldBeGreaterThanOrEqualTo(scheduled);
        }


        public enum _Status
        {
            SUCCEEDED = 0,
            FAILED = 1,
            SCHEDULED = 2
        }
    }
}