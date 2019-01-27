﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fetcho.Common;
using log4net;

namespace Fetcho
{
    public class Fetcho
    {
        const int MaxConcurrentFetches = 10000;
        const int HowOftenToReportStatusInMilliseconds = 30000;
        static readonly ILog log = LogManager.GetLogger(typeof(Fetcho));
        int activeFetches = 0;
        public FetchoConfiguration Configuration { get; set; }
        SemaphoreSlim fetchLock = new SemaphoreSlim(MaxConcurrentFetches);
        TextWriter requeueWriter = null;
        TextWriter outputWriter = null;
        TextReader inputReader = null;

        public Fetcho(FetchoConfiguration config)
        {
            Configuration = config;
        }

        public async Task Process()
        {
            requeueWriter = GetRequeueWriter();
            inputReader = GetInputReader();
            outputWriter = GetOutputWriter();
            await FetchUris();
        }

        async Task FetchUris()
        {
            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            var u = ParseQueueItem(inputReader.ReadLine());

            while (u != null)
            {
                await fetchLock.WaitAsync(cancellationToken);

                if (!u.HasAnIssue)
                {
                    var t = FetchQueueItem(u, cts.Token);
                }

                u = ParseQueueItem(inputReader.ReadLine());
            }

            await Task.Delay(HowOftenToReportStatusInMilliseconds);

            while (true)
            {
                await Task.Delay(HowOftenToReportStatusInMilliseconds, cancellationToken);
                log.InfoFormat("STATUS: Active Fetches {0}", activeFetches);
                if (activeFetches == 0)
                    return;
            }
        }

        QueueItem ParseQueueItem(string line)
        {
            try
            {
                if (Configuration.InputRawUrls)
                    return new QueueItem() { TargetUri = new Uri(line) };
                else
                    return QueueItem.Parse(line);
            }
            catch (Exception)
            {
                return null;
            }
        }
        async Task FetchQueueItem(QueueItem item, CancellationToken cancellationToken)
        {
            try
            {
                Interlocked.Increment(ref activeFetches);
                await ResourceFetcher.FetchFactory(item.TargetUri, Console.Out, DateTime.MinValue, cancellationToken);
            }
            catch (TimeoutException)
            {
                log.InfoFormat("Waited too long to be able to fetch {0}", item.TargetUri);
                OutputItemForRequeuing(item);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                fetchLock.Release();
                Interlocked.Decrement(ref activeFetches);
            }
        }

        void OutputItemForRequeuing(QueueItem item) => requeueWriter?.WriteLine(item);

        /// <summary>
        /// Open the data stream from either a specific file or STDIN
        /// </summary>
        /// <returns>A TextReader if successful</returns>
        TextReader GetInputReader()
        {
            // if there's no file argument, read from STDIN
            if (String.IsNullOrWhiteSpace(Configuration.UriSourceFilePath))
                return Console.In;

            var fs = new FileStream(Configuration.UriSourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sr = new StreamReader(fs);

            return sr;
        }

        TextWriter GetOutputWriter() => Console.Out;

        TextWriter GetRequeueWriter()
        {
            if (String.IsNullOrEmpty(Configuration.RequeueUrlsFilePath))
                return null;
            return new StreamWriter(new FileStream(Configuration.RequeueUrlsFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read));
        }
    }
}

