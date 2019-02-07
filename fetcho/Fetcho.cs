﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Fetcho.Common;
using log4net;

namespace Fetcho
{
    /// <summary>
    /// Fetches URIs from the input stream
    /// </summary>
    public class Fetcho
    {
        const int MaxConcurrentFetches = 5000;
        const int PressureReliefThreshold = (MaxConcurrentFetches * 3) / 10; // 80%
        const int HowOftenToReportStatusInMilliseconds = 30000;
        const int TaskStartupWaitTimeInMilliseconds = 360000;
        const int MinPressureReliefValveWaitTimeInMilliseconds = 10000;
        const int MaxPressureReliefValveWaitTimeInMilliseconds = 120000;

        static readonly ILog log = LogManager.GetLogger(typeof(Fetcho));
        int completedFetches = 0;
        public FetchoConfiguration Configuration { get; set; }
        SemaphoreSlim fetchLock = new SemaphoreSlim(MaxConcurrentFetches);
        TextWriter requeueWriter = null;
        XmlWriter outputWriter = null;
        TextReader inputReader = null;
        PressureReliefValve<QueueItem> valve = null;
        Random random = new Random(DateTime.Now.Millisecond);

        public Fetcho(FetchoConfiguration config)
        {
            Configuration = config;
            valve = CreatePressureReliefValve();
        }

        public async Task Process()
        {
            log.Info("Fetcho.Process() commenced");
            requeueWriter = GetRequeueWriter();
            inputReader = GetInputReader();

            OpenNewOutputWriter();
            await FetchUris();
            CloseOutputWriter();
            log.Info("Fetcho.Process() complete");
        }

        private async Task FetchUris()
        {
            var u = ParseQueueItem(inputReader.ReadLine());

            while (u != null)
            {
                while (!await fetchLock.WaitAsync(TaskStartupWaitTimeInMilliseconds))
                    log.InfoFormat("Been waiting a while to fire up a new fetch. Active: {0}, complete: {1}", valve.TasksInValve, completedFetches);

                if (u.HasAnIssue)
                    log.InfoFormat("QueueItem has an issue:{0}", u);
                else
                {
                    var t = FetchQueueItem(u);
                }

                u = ParseQueueItem(inputReader.ReadLine());
            }

            await Task.Delay(HowOftenToReportStatusInMilliseconds);

            while (true)
            {
                await Task.Delay(HowOftenToReportStatusInMilliseconds);
                log.InfoFormat("STATUS: Active Fetches {0}, Completed {1}", valve.TasksInValve, completedFetches);
                if (valve.TasksInValve <= 0)
                    return;
            }
        }

        private QueueItem ParseQueueItem(string line)
        {
            try
            {
                if (Configuration.InputRawUrls)
                    return new QueueItem() { TargetUri = new Uri(line), Priority = 0 };
                else
                    return QueueItem.Parse(line);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task FetchQueueItem(QueueItem item)
        {
            try
            {
                if (!await valve.WaitToEnter(item))
                {
                    log.InfoFormat("Waited too long to be able to fetch {0}", item.TargetUri);
                    OutputItemForRequeuing(item);
                }
                else
                {
                    try
                    {
                        await ResourceFetcher.FetchFactory(
                            item.TargetUri,
                            outputWriter,
                            Configuration.BlockProvider,
                            DateTime.MinValue
                            );
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                    finally
                    {
                        valve.Exit(item);
                        Interlocked.Increment(ref completedFetches);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                fetchLock.Release();
            }
        }

        /// <summary>
        /// Output the item for requeuing
        /// </summary>
        /// <param name="item"></param>
        private void OutputItemForRequeuing(QueueItem item) => requeueWriter?.WriteLine(item);

        /// <summary>
        /// Open the data stream from either a specific file or STDIN
        /// </summary>
        /// <returns>A TextReader if successful</returns>
        private TextReader GetInputReader()
        {
            // if there's no file argument, read from STDIN
            if (String.IsNullOrWhiteSpace(Configuration.UriSourceFilePath))
                return Console.In;

            var fs = new FileStream(Configuration.UriSourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sr = new StreamReader(fs);

            return sr;
        }

        /// <summary>
        /// Get the stream where we're writing the internet out to
        /// </summary>
        /// <returns></returns>
        private XmlWriter GetOutputWriter()
        {
            TextWriter writer = null;

            if (String.IsNullOrWhiteSpace(Configuration.OutputDataFilePath))
                writer = Console.Out;
            else
            {
                string filename = Utility.CreateNewFileIndexNameIfExists(Configuration.OutputDataFilePath);
                writer = new StreamWriter(new FileStream(filename, FileMode.Open, FileAccess.Write, FileShare.Read));
            }

            var settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.NewLineHandling = NewLineHandling.Replace;
            return XmlWriter.Create(writer, settings);
        }

        private void OpenNewOutputWriter()
        {
            outputWriter = GetOutputWriter();
            outputWriter.WriteStartDocument();
            outputWriter.WriteStartElement("resources");
        }

        private void CloseOutputWriter()
        {
            outputWriter.WriteEndElement();
            outputWriter.WriteEndDocument();
            outputWriter.Flush();

            if (!String.IsNullOrWhiteSpace(Configuration.OutputDataFilePath))
            {
                outputWriter.Close();
                outputWriter.Dispose();
            }
        }

        private TextWriter GetRequeueWriter()
        {
            if (String.IsNullOrEmpty(Configuration.RequeueUrlsFilePath))
                return null;
            return new StreamWriter(new FileStream(Configuration.RequeueUrlsFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read));
        }

        /// <summary>
        /// Creates the pressure relief valve to throw out tasks
        /// </summary>
        /// <returns></returns>
        private PressureReliefValve<QueueItem> CreatePressureReliefValve()
        {
            var prv = new PressureReliefValve<QueueItem>(PressureReliefThreshold);

            prv.WaitFunc = async (item) =>
                await HostCacheManager.WaitToFetch(
                        (await Utility.GetHostIPAddress( item.TargetUri)),
                        GetRandomReliefTimeout() // randomised to spread the timeouts evenly to avoid bumps
                    );

            return prv;
        }

        /// <summary>
        /// Relief timeout random
        /// </summary>
        /// <returns></returns>
        private int GetRandomReliefTimeout() =>
            random.Next(
                MinPressureReliefValveWaitTimeInMilliseconds,
                MaxPressureReliefValveWaitTimeInMilliseconds
                );
    }
}

