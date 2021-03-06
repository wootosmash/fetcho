﻿using Fetcho.Common;
using log4net;
using System;
using System.Threading.Tasks;

namespace reado
{
    public class ListUrisInPacketConsumer : WebDataPacketConsumer
    {
        static readonly ILog log = LogManager.GetLogger(typeof(ListUrisInPacketConsumer));

        public Uri CurrentUri;
        public ContentType ContentType;

        public override string Name { get => "List URIs"; }
        public override bool ProcessesRequest { get => true; }

        public override async Task ProcessRequest(string request)
        {
            CurrentUri = WebDataPacketReader.GetUriFromRequestString(request);
            Console.WriteLine(CurrentUri);
        }

        public override void NewResource()
        {
            CurrentUri = null;
            ContentType = null;
        }
    }
}
