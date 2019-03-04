﻿using Fetcho.Common;
using Fetcho.Common.DataFlow;
using log4net;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Fetcho
{
    class Program
    {
        const int DefaultBufferBlockLimit = 10;

        public static void Main(string[] args)
        {
            if ( args.Length < 2 )
            {
                Usage();
                return;
            }

            string path = args[0];
            int.TryParse(args[1], out int startPacketIndex);

            // turn on log4net
            log4net.Config.XmlConfigurator.Configure();

            var log = LogManager.GetLogger(typeof(Program));

            // catch all errors and log them
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => log.Error(eventArgs.ExceptionObject);

            // ignore all certificate validation issues
            ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

            // console encoding will now be unicode
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // configure fetcho
            var cfg = new FetchoConfiguration();
            cfg.DataSourcePath = path;

            // setup the block provider we want to use
            cfg.BlockProvider = new DefaultBlockProvider();

            // configure queueo
            cfg.QueueOrderingModel = new NaiveQueueOrderingModel();

            // buffers to connect the seperate tasks together
            BufferBlock<IEnumerable<QueueItem>> prioritisationBuffer = CreateBufferBlock(DefaultBufferBlockLimit);
            // really beef this up since it takes for ever to get here
            BufferBlock<IEnumerable<QueueItem>> fetchQueueBuffer = CreateBufferBlock(DefaultBufferBlockLimit*1000);
            BufferBlock<IEnumerable<QueueItem>> requeueBuffer = CreateBufferBlock(DefaultBufferBlockLimit);
            BufferBlock<IEnumerable<QueueItem>> rejectsBuffer = CreateBufferBlock(DefaultBufferBlockLimit);

            // fetcho!
            var readLinko = new ReadLinko(cfg, prioritisationBuffer, startPacketIndex);
            var queueo = new Queueo(cfg, prioritisationBuffer, fetchQueueBuffer, rejectsBuffer);
            var fetcho = new Fetcho(cfg, fetchQueueBuffer, requeueBuffer);
            var requeueWriter = new BufferBlockObjectFileWriter<IEnumerable<QueueItem>>(cfg.DataSourcePath, "requeue", requeueBuffer);
            var rejectsWriter = new BufferBlockObjectFileWriter<IEnumerable<QueueItem>>(cfg.DataSourcePath, "rejects", rejectsBuffer);

            // execute
            var tasks = new List<Task>();

            tasks.Add(fetcho.Process());

            Task.Delay(1000).GetAwaiter().GetResult();
            tasks.Add(requeueWriter.Process());
            tasks.Add(rejectsWriter.Process());
            Task.Delay(1000).GetAwaiter().GetResult();
            tasks.Add(queueo.Process());
            Task.Delay(1000).GetAwaiter().GetResult();
            tasks.Add(readLinko.Process());

            Task.WaitAll(tasks.ToArray());
        }

        private static void Usage()
        {
            Console.WriteLine("fetcho path start_index");
        }

        private static BufferBlock<IEnumerable<QueueItem>> CreateBufferBlock(int boundedCapacity) =>
            new BufferBlock<IEnumerable<QueueItem>>(new DataflowBlockOptions()
            {
                BoundedCapacity = boundedCapacity
            });
    }
}