﻿#nullable disable
using System;

// ReSharper disable CheckNamespace

namespace Shashlik.EventBus
{
    /// <summary>
    /// 事件处理类描述信息
    /// </summary>
    public class EventHandlerDescriptor
    {
        /// <summary>
        /// 事件处理名称(NameRuler规则计算后)
        /// </summary>
        public string EventHandlerName { get; init; }

        /// <summary>
        /// 事件名称(NameRuler规则计算后)
        /// </summary>
        public string EventName { get; init; }

        /// <summary>
        /// 事件类型
        /// </summary>
        public Type EventType { get; init; }

        /// <summary>
        /// 事件处理类名称,将同时注册为service和impl
        /// </summary>
        public Type EventHandlerType { get; init; }
    }
}