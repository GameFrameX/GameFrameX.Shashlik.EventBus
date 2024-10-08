﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.EventBus.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shashlik.EventBus;

namespace GameFrameX.EventBus.DefaultImpl
{
    public class DefaultPublishHandler : IPublishHandler
    {
        public DefaultPublishHandler(IOptions<EventBusOptions> options, IMessageSender messageSender,
            IMessageStorage                                    messageStorage,
            ILogger<DefaultPublishHandler>                     logger, IMessageSerializer messageSerializer)
        {
            Options           = options;
            MessageSender     = messageSender;
            MessageStorage    = messageStorage;
            Logger            = logger;
            MessageSerializer = messageSerializer;
        }

        private IMessageSender                 MessageSender     { get; }
        private IMessageStorage                MessageStorage    { get; }
        private IOptions<EventBusOptions>      Options           { get; }
        private ILogger<DefaultPublishHandler> Logger            { get; }
        private IMessageSerializer             MessageSerializer { get; }

        public async Task<HandleResult> HandleAsync(
            MessageTransferModel messageTransferModel,
            MessageStorageModel  messageStorageModel,
            CancellationToken    cancellationToken = default)
        {
            return await HandleAsync(messageStorageModel.Id, messageTransferModel, messageStorageModel, false,
                                     cancellationToken);
        }

        public async Task<HandleResult> LockingHandleAsync(string id, CancellationToken cancellationToken = default)
        {
            return await HandleAsync(id, null, null, true, cancellationToken);
        }

        private async Task<HandleResult> HandleAsync(string id, MessageTransferModel messageTransferModel, MessageStorageModel messageStorageModel, bool requireLock, CancellationToken cancellationToken = default)
        {
            try
            {
                // 尝试锁定
                if (!requireLock || await MessageStorage.TryLockPublishedAsync(
                        id, DateTimeOffset.Now.AddSeconds(Options.Value.LockTime),
                        cancellationToken).ConfigureAwait(false)
                   )
                {
                    messageStorageModel ??= await MessageStorage.FindPublishedByIdAsync(id, cancellationToken);
                    if (messageStorageModel is null)
                    {
                        Logger.LogDebug($"[EventBus] published messaged \"{id}\" not found");
                        return new HandleResult(true);
                    }

                    messageTransferModel ??= new MessageTransferModel
                    {
                        EventName   = messageStorageModel.EventName,
                        Environment = messageStorageModel.Environment,
                        MsgId       = messageStorageModel.MsgId,
                        MsgBody     = messageStorageModel.EventBody,
                        Items = MessageSerializer.Deserialize<IDictionary<string, string>>(messageStorageModel
                                                                                               .EventItems),
                        SendAt  = messageStorageModel.CreateTime,
                        DelayAt = messageStorageModel.DelayAt,
                    };

                    // 这里可能存在的是消息发送成功,数据库更新失败,那么就可能存在重复发送的情况,这个需要消费方自行冥等处理
                    // 事务已提交,执行消息发送和更新状态
                    await MessageSender.SendAsync(messageTransferModel).ConfigureAwait(false);

                    // 消息发送没问题就更新数据库状态
                    await MessageStorage.UpdatePublishedAsync(
                                            messageStorageModel.Id,
                                            MessageStatus.Succeeded,
                                            ++messageStorageModel.RetryCount,
                                            DateTimeOffset.Now.AddHours(Options.Value.SucceedExpireHour),
                                            cancellationToken)
                                        .ConfigureAwait(false);
                }
                // 锁定失败,可能有其它节点的重试器在执行,直接返回true,不管了

                return new HandleResult(true);
            }
            catch (Exception ex)
            {
                if (messageStorageModel is null)
                {
                    // 这种一般只会是数据库查询/连接异常,不管了,交给重试器执行
                    Logger.LogError(ex, $"[EventBus]: {ex.Message}");
                    return new HandleResult(true);
                }

                try
                {
                    await MessageStorage.UpdatePublishedAsync(
                                            messageStorageModel.Id,
                                            MessageStatus.Failed,
                                            ++messageStorageModel.RetryCount,
                                            null,
                                            cancellationToken)
                                        .ConfigureAwait(false);
                }
                catch (Exception exInner)
                {
                    Logger.LogError(exInner, "[EventBus] update published message occur error");
                }

                Logger.LogError(ex,
                                $"[EventBus] message publish error, will try again later, event: {messageStorageModel.EventName},  msgId: {messageStorageModel.MsgId}");

                return new HandleResult(false, messageStorageModel);
            }
        }
    }
}