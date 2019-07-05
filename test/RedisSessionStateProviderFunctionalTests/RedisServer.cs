//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;

namespace Microsoft.Web.Redis.FunctionalTests
{
    internal class RedisServer : IDisposable
    {
        private RedisInside.Redis redisInside;

        public RedisServer()
        {
            Restart();
        }

        public void Restart()
        {
            redisInside?.Dispose();
            redisInside = new RedisInside.Redis(configuration => configuration.Port(0));
        }

        public void Dispose()
        {
            try
            {
                redisInside?.Dispose();
            }
            catch
            { }
        }
    }
}
