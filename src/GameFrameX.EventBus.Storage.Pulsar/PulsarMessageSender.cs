﻿using System;
using System.Threading.Tasks;
using GameFrameX.EventBus.Utils;
using Microsoft.Extensions.Logging;
using Shashlik.EventBus;

// ReSharper disable TemplateIsNotCompileTimeConstantProblem

namespace GameFrameX.EventBus.Storage.Pulsar;

/// <summary>
///     消息发送处理类
/// </summary>
public class PulsarMessageSender : IMessageSender
{
    public PulsarMessageSender(
        IPulsarConnection            connection,
        ILogger<PulsarMessageSender> logger,
        IMessageSerializer           messageSerializer)
    {
        Connection        = connection;
        Logger            = logger;
        MessageSerializer = messageSerializer;
    }

    private ILogger<PulsarMessageSender> Logger            { get; }
    private IMessageSerializer           MessageSerializer { get; }
    private IPulsarConnection            Connection        { get; }

    public async Task SendAsync(MessageTransferModel message)
    {
        try
        {
            var producer       = Connection.GetProducer(message.EventName);
            var messageBuilder = producer.NewMessage(MessageSerializer.SerializeToBytes(message));
            var result         = await producer.SendAsync(messageBuilder);
            if (result?.Type != null)
            {
                Logger.LogDebug($"[EventBus-Pulsar] send msg success: {message}");
            }
        }
        catch (Exception e)
        {
            throw new EventBusException(
                $"[EventBus-Pulsar] send msg fail,  message: {message}", e);
        }
    }
}