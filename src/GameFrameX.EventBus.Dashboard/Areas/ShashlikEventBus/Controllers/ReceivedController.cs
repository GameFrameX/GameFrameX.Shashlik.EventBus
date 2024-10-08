﻿using GameFrameX.EventBus.Dashboard.Areas.ShashlikEventBus.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GameFrameX.EventBus.Dashboard.Areas.ShashlikEventBus.Controllers;

public class ReceivedController : BaseDashboardController
{
    private readonly IMessageStorage _messageStorage;

    public ReceivedController(IOptionsMonitor<EventBusDashboardOption> options, IMessageStorage messageStorage) : base(options)
    {
        _messageStorage = messageStorage;
    }


    public async Task<IActionResult> Index(string eventName, string eventHandlerName, MessageStatus status, int pageSize = 20, int pageIndex = 1)
    {
        ViewBag.Title = "Received";
        ViewBag.Page  = "Received";
        var model = new MessageViewModel
        {
            StatusCount = await _messageStorage.GetReceivedMessageStatusCountAsync(CancellationToken.None),
        };
        if (status == MessageStatus.None && model.StatusCount.Keys.Count > 0)
        {
            status = model.StatusCount.Keys.First();
        }

        model.Messages = await _messageStorage.SearchReceivedAsync(eventName, eventHandlerName, status,
                                                                   (pageIndex - 1) * pageSize,
                                                                   pageSize, CancellationToken.None);
        model.PageIndex        = pageIndex;
        model.PageSize         = pageSize;
        model.EventName        = eventName;
        model.EventHandlerName = eventHandlerName;
        var total = 0M;
        if (status != MessageStatus.None)
        {
            total = model.StatusCount.TryGetValue(status, out var value) ? value : total;
        }

        model.TotalPage = Convert.ToInt32(Math.Ceiling(total / pageSize));
        return View("Messages", model);
    }

    public async Task Retry(string[] ids, [FromServices] IReceivedMessageRetryProvider receivedMessageRetryProvider)
    {
        if (ids == null)
        {
            return;
        }

        foreach (var id in ids)
        {
            try
            {
                await receivedMessageRetryProvider.RetryAsync(id, CancellationToken.None);
            }
            catch (Exception)
            {
                //
            }
        }
    }
}