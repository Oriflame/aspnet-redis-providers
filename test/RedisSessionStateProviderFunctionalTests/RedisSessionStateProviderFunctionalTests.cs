//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using Microsoft.Web.Redis.Tests;
using StackExchange.Redis;
using System;
using System.Collections.Specialized;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using System.Threading;
using System.Web;
using Microsoft.AspNet.SessionState;
using Oriflame.Web.Redis;

namespace Microsoft.Web.Redis.FunctionalTests
{
    public class RedisSessionStateProviderFunctionalTests
    {
        private class RawStringSerializer : ISerializer
        {
            public byte[] Serialize(object data)
            {
                return Encoding.UTF8.GetBytes(data.ToString());
            }

            public object Deserialize(byte[] data)
            {
                return Encoding.UTF8.GetString(data);
            }
        }

        private string ResetRedisConnectionWrapperAndConfiguration()
        {
            RedisConnectionWrapper.sharedConnection = null;
            RedisSessionStateProvider.configuration = Utility.GetDefaultConfigUtility();
            //RedisSessionStateProvider.redisUtility =  new RedisUtility(RedisSessionStateProvider.configuration);
            return Guid.NewGuid().ToString();
        }

        private void DisposeRedisConnectionWrapper()
        {
            RedisConnectionWrapper.sharedConnection = null;
        }

        [Fact]
        public async Task SessionWriteCycle_Valid()
        {
            using (RedisServer redisServer = new RedisServer())
            {
                string sessionId = ResetRedisConnectionWrapperAndConfiguration();

                // Inserting empty session with "SessionStateActions.InitializeItem" flag into redis server
                RedisSessionStateProvider ssp = new Oriflame.Web.Redis.RedisSessionStateProvider();
                await ssp.CreateUninitializedItemAsync(null, sessionId, (int)RedisSessionStateProvider.configuration.SessionTimeout.TotalMinutes, CancellationToken.None);

                // Get write lock and session from cache
                GetItemResult data = await ssp.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

                // Get actual connection and varify lock and session timeout
                IDatabase actualConnection = GetRealRedisConnection();
                Assert.Equal(data.LockId.ToString(), actualConnection.StringGet(ssp.cache.Keys.LockKey).ToString());
                Assert.Equal(((int)RedisSessionStateProvider.configuration.SessionTimeout.TotalSeconds).ToString(), actualConnection.HashGet(ssp.cache.Keys.InternalKey, "SessionTimeout").ToString());

                // setting data as done by any normal session operation
                data.Item.Items["key"] = "value";

                // session update
                await ssp.SetAndReleaseItemExclusiveAsync(null, sessionId, data.Item, data.LockId, false, CancellationToken.None);
                Assert.Equal(1, actualConnection.HashGetAll(ssp.cache.Keys.DataKey).Length);

                // reset sessions timoue
                await ssp.ResetItemTimeoutAsync(null, sessionId, CancellationToken.None);

                // End request
                await ssp.EndRequestAsync(null);

                // remove data and lock from redis
                DisposeRedisConnectionWrapper();
            }
        }

        [Fact]
        public async Task SessionSetVersion_WithLocking_Valid()
        {
            using (new RedisServer())
            {
                var sessionId = ResetRedisConnectionWrapperAndConfiguration();

                RedisSessionStateProvider.configuration = null;

                // Inserting empty session with "SessionStateActions.InitializeItem" flag into redis server
                var ssp = new Oriflame.Web.Redis.RedisSessionStateProvider();

                var config = new NameValueCollection
                {
                    ["redisSerializerType"] = typeof(RawStringSerializer).AssemblyQualifiedName,
                    ["port"] = "0",
                    ["ssl"] = "false",
                    [SessionVersionFromApplicationProvider.VersionConfigAttributeName] = "1.2.3.4",
                    [Oriflame.Web.Redis.RedisSessionStateProvider.SessionVersionProviderTypeAttributeName] = typeof(SessionVersionFromApplicationProvider).FullName
                };

                ssp.Initialize("ssp", config);

                await ssp.CreateUninitializedItemAsync(null, sessionId, (int)RedisSessionStateProvider.configuration.SessionTimeout.TotalMinutes, CancellationToken.None);

                // Get write lock and session from cache
                var data = await ssp.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

                // Get actual connection and verify lock and session timeout
                var actualConnection = GetRealRedisConnection();
                Assert.Equal(data.LockId.ToString(), actualConnection.StringGet(ssp.cache.Keys.LockKey).ToString());
                Assert.Equal(((int)RedisSessionStateProvider.configuration.SessionTimeout.TotalSeconds).ToString(), actualConnection.HashGet(ssp.cache.Keys.InternalKey, "SessionTimeout").ToString());

                // setting data as done by any normal session operation
                data.Item.Items["key"] = "value";

                // session update
                await ssp.SetAndReleaseItemExclusiveAsync(null, sessionId, data.Item, data.LockId, false, CancellationToken.None);
                Assert.Equal(2, actualConnection.HashGetAll(ssp.cache.Keys.DataKey).Length);

                // simulation of invalid version
                actualConnection.HashSet(ssp.cache.Keys.DataKey,
                    new[] { new HashEntry(SessionVersionFromApplicationProvider.SessionVersionKey, "modified-by-test") });

                var ssp2 = new Oriflame.Web.Redis.RedisSessionStateProvider();
                ssp2.Initialize("ssp2", config);
                var data2 = await ssp2.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

                Assert.Equal(1, data2.Item.Items.Count);

                //var items2 = data2.Item.Items.Keys;

                data2.Item.Items["test2"] = "test value";

                await ssp2.SetAndReleaseItemExclusiveAsync(null, sessionId, data2.Item, data2.LockId, false, CancellationToken.None);
                Assert.Equal(2, actualConnection.HashGetAll(ssp2.cache.Keys.DataKey).Length);
                DisposeRedisConnectionWrapper();
            }
        }

        [Fact]
        public async Task SessionSetVersion_WithoutLocking_Valid()
        {
            using (new RedisServer())
            {
                var sessionId = ResetRedisConnectionWrapperAndConfiguration();

                RedisSessionStateProvider.configuration = null;

                // Inserting empty session with "SessionStateActions.InitializeItem" flag into redis server
                var ssp = new Oriflame.Web.Redis.RedisSessionStateProvider();

                var config = new NameValueCollection
                {
                    ["redisSerializerType"] = typeof(RawStringSerializer).AssemblyQualifiedName,
                    ["port"] = "0",
                    ["ssl"] = "false",
                    [SessionVersionFromApplicationProvider.VersionConfigAttributeName] = "1.2.3.4",
                    [Oriflame.Web.Redis.RedisSessionStateProvider.SessionVersionProviderTypeAttributeName] = typeof(SessionVersionFromApplicationProvider).FullName
                };

                ssp.Initialize("ssp", config);

                await ssp.CreateUninitializedItemAsync(null, sessionId, (int)RedisSessionStateProvider.configuration.SessionTimeout.TotalMinutes, CancellationToken.None);

                // Get write lock and session from cache
                var data = await ssp.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

                // Get actual connection and verify lock and session timeout
                var actualConnection = GetRealRedisConnection();
                Assert.Equal(data.LockId.ToString(), actualConnection.StringGet(ssp.cache.Keys.LockKey).ToString());
                Assert.Equal(((int)RedisSessionStateProvider.configuration.SessionTimeout.TotalSeconds).ToString(), actualConnection.HashGet(ssp.cache.Keys.InternalKey, "SessionTimeout").ToString());

                // setting data as done by any normal session operation
                data.Item.Items["key"] = "value";

                // session update
                await ssp.SetAndReleaseItemExclusiveAsync(null, sessionId, data.Item, data.LockId, false, CancellationToken.None);
                Assert.Equal(2, actualConnection.HashGetAll(ssp.cache.Keys.DataKey).Length);

                // simulation of invalid version
                actualConnection.HashSet(ssp.cache.Keys.DataKey,
                    new[] { new HashEntry(SessionVersionFromApplicationProvider.SessionVersionKey, "modified-by-test") });

                var ssp2 = new Oriflame.Web.Redis.RedisSessionStateProvider();
                ssp2.Initialize("ssp2", config);
                var data2 = await ssp2.GetItemAsync(null, sessionId, CancellationToken.None);

                Assert.Equal(1, data2.Item.Items.Count);

                // remove data and lock from redis
                DisposeRedisConnectionWrapper();
            }
        }

        [Fact]
        public async Task SessionReadCycle_Valid()
        {
            using (RedisServer redisServer = new RedisServer())
            {
                string sessionId = ResetRedisConnectionWrapperAndConfiguration();

                // Inserting empty session with "SessionStateActions.InitializeItem" flag into redis server
                RedisSessionStateProvider ssp = new Oriflame.Web.Redis.RedisSessionStateProvider();
                await ssp.CreateUninitializedItemAsync(null, sessionId, (int)RedisSessionStateProvider.configuration.SessionTimeout.TotalMinutes, CancellationToken.None);

                // Get write lock and session from cache
                GetItemResult data = await ssp.GetItemAsync(null, sessionId, CancellationToken.None);

                // Get actual connection and varify lock and session timeout
                IDatabase actualConnection = GetRealRedisConnection();
                Assert.True(actualConnection.StringGet(ssp.cache.Keys.LockKey).IsNull);
                Assert.Equal(((int)RedisSessionStateProvider.configuration.SessionTimeout.TotalSeconds).ToString(), actualConnection.HashGet(ssp.cache.Keys.InternalKey, "SessionTimeout").ToString());

                // reset sessions timoue
                await ssp.ResetItemTimeoutAsync(null, sessionId, CancellationToken.None);

                // End request
                await ssp.EndRequestAsync(null);

                // remove data and lock from redis
                DisposeRedisConnectionWrapper();
            }
        }

        [Fact]
        public async Task SessionTimoutChangeFromGlobalAspx()
        {
            using (RedisServer redisServer = new RedisServer())
            {
                string sessionId = ResetRedisConnectionWrapperAndConfiguration();

                // Inserting empty session with "SessionStateActions.InitializeItem" flag into redis server
                RedisSessionStateProvider ssp = new Oriflame.Web.Redis.RedisSessionStateProvider();
                await ssp.CreateUninitializedItemAsync(null, sessionId, (int)RedisSessionStateProvider.configuration.SessionTimeout.TotalMinutes, CancellationToken.None);

                // Get write lock and session from cache
                GetItemResult data = await ssp.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);

                // Get actual connection and varify lock and session timeout
                IDatabase actualConnection = GetRealRedisConnection();
                Assert.Equal(data.LockId.ToString(), actualConnection.StringGet(ssp.cache.Keys.LockKey).ToString());
                Assert.Equal(((int)RedisSessionStateProvider.configuration.SessionTimeout.TotalSeconds).ToString(), actualConnection.HashGet(ssp.cache.Keys.InternalKey, "SessionTimeout").ToString());

                // setting data as done by any normal session operation
                data.Item.Items["key"] = "value";
                data.Item.Timeout = 5;

                // session update
                await ssp.SetAndReleaseItemExclusiveAsync(null, sessionId, data.Item, data.LockId, false, CancellationToken.None);
                Assert.Equal(1, actualConnection.HashGetAll(ssp.cache.Keys.DataKey).Length);
                Assert.Equal("300", actualConnection.HashGet(ssp.cache.Keys.InternalKey, "SessionTimeout").ToString());

                // reset sessions timoue
                await ssp.ResetItemTimeoutAsync(null, sessionId, CancellationToken.None);

                // End request
                await ssp.EndRequestAsync(null);

                // Verify that GetItemExclusive returns timeout from redis
                GetItemResult data_1 = await ssp.GetItemExclusiveAsync(null, sessionId, CancellationToken.None);
                Assert.Equal(5, data.Item.Timeout);

                // remove data and lock from redis
                DisposeRedisConnectionWrapper();
            }
        }

        [Fact]
        public async Task ReleaseItemExclusiveWithNullLockId()
        {
            using (RedisServer redisServer = new RedisServer())
            {
                string sessionId = ResetRedisConnectionWrapperAndConfiguration();
                RedisSessionStateProvider ssp = new Oriflame.Web.Redis.RedisSessionStateProvider();
                await ssp.ReleaseItemExclusiveAsync(null, sessionId, null, CancellationToken.None);
                DisposeRedisConnectionWrapper();
            }
        }

        [Fact]
        public async Task RemoveItemWithNullLockId()
        {
            using (RedisServer redisServer = new RedisServer())
            {
                string sessionId = ResetRedisConnectionWrapperAndConfiguration();
                RedisSessionStateProvider ssp = new Oriflame.Web.Redis.RedisSessionStateProvider();
                await ssp.RemoveItemAsync(null, sessionId, null, null, CancellationToken.None);
                DisposeRedisConnectionWrapper();
            }
        }

        private IDatabase GetRealRedisConnection()
        {
            return RedisConnectionWrapper.sharedConnection.Connection;
        }
    }
}
