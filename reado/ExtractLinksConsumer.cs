﻿using Fetcho.Common;
using Fetcho.ContentReaders;
using log4net;
using System;
using System.IO;

namespace Fetcho
{
    internal class ExtractLinksConsumer : IWebDataPacketConsumer
    {
        static readonly ILog log = LogManager.GetLogger(typeof(ExtractLinksConsumer));

        public Uri CurrentUri;
        public ContentType ContentType;

        public string Name { get => "Extract Links"; }
        public bool ProcessesRequest { get => true; }
        public bool ProcessesResponse { get => true; }
        public bool ProcessesException { get => false; }

        public void ProcessException(string exception)
        {
        }

        public void ProcessRequest(string request)
        {
            CurrentUri = WebDataPacketReader.GetUriFromRequestString(request);
        }

        public void ProcessResponseHeaders(string responseHeaders)
        {
            ContentType = WebDataPacketReader.GetContentTypeFromResponseHeaders(responseHeaders);
        }

        public void ProcessResponseStream(Stream dataStream)
        {
            if (dataStream == null) return;
            var extractor = GuessLinkExtractor(dataStream);
            if (extractor != null)
                OutputUris(extractor);
        }

        public void NewResource()
        {
            CurrentUri = null;
            ContentType = null;
        }

        public void PacketClosed()
        {

        }

        public void PacketOpened()
        {

        }

        public void ReadingException(Exception ex) { }

        private ILinkExtractor GuessLinkExtractor(Stream dataStream)
        {
            var ms = new MemoryStream();
            dataStream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);

            if (ContentType.IsUnknownOrNull(ContentType))
                ContentType = ContentType.Guess(ms);

            ms.Seek(0, SeekOrigin.Begin);

            if (ContentType.IsUnknownOrNull(ContentType) || ContentType.MediaType == "text")
                return new TextFileLinkExtractor(CurrentUri, new StreamReader(ms));
            else
            {
                ms.Dispose();
                ms = null;
                log.InfoFormat("No link extractor for content type: {0}, from {1}", ContentType, CurrentUri);
                return null;
            }
        }

        private void OutputUris(ILinkExtractor reader)
        {
            if (reader == null) return;

            Uri uri = reader.NextUri();

            while (uri != null)
            {
                Console.WriteLine("{0}\t{1}", reader.CurrentSourceUri, uri);

                uri = reader.NextUri();
            }
        }
    }

}
