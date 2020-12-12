﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Shashlik.EventBus.MemoryStorage.Tests
{
    public class TestBase : IClassFixture<TestWebApplicationFactory<TestStartup>>, IDisposable
    {
        protected TestWebApplicationFactory<TestStartup> Factory { get; }
        protected HttpClient HttpClient { get; }
        protected IServiceScope ServiceScope { get; }

        public TestBase(TestWebApplicationFactory<TestStartup> factory, ITestOutputHelper testOutputHelper)
        {
            Factory = factory;
            factory.Output = testOutputHelper;
            HttpClient = factory.CreateClient();
            ServiceScope = factory.Services.CreateScope();
        }

        protected T GetService<T>()
        {
            return ServiceScope.ServiceProvider.GetService<T>();
        }

        protected IEnumerable<T> GetServices<T>()
        {
            return ServiceScope.ServiceProvider.GetServices<T>();
        }

        public virtual void Dispose()
        {
            ServiceScope.Dispose();
        }
    }
}