﻿namespace Fetcho.queueo
{
    class Program
    {
        public static void Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();
            var config = new QueueoConfiguration(args);
            config.QueueOrderingModel = new NaiveQueueOrderingModel();

            var queueo = new Queueo(config);
            queueo.Process().GetAwaiter().GetResult();
        }
    }
}