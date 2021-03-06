﻿using Fetcho.Common;
using Fetcho.ContentReaders;
using log4net;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Fetcho
{
    public class ExtractLinksConsumer : WebDataPacketConsumer
    {
        static readonly ILog log = LogManager.GetLogger(typeof(ExtractLinksConsumer));

        public Uri CurrentUri;
        public ContentType ContentType;

        public override string Name { get => "Extract Links"; }
        public override bool ProcessesRequest { get => true; }
        public override bool ProcessesResponse { get => true; }

        public override async Task ProcessRequest(string request)
            => CurrentUri = WebDataPacketReader.GetUriFromRequestString(request);

        public override async Task ProcessResponseHeaders(string responseHeaders)
            => ContentType = WebDataPacketReader.GetContentTypeFromResponseHeaders(responseHeaders);

        public override async Task ProcessResponseStream(Stream dataStream)
        {
            if (dataStream == null) return;
            var extractor = GuessLinkExtractor(dataStream);
            if (extractor != null)
                OutputUris(extractor);
        }

        public override void NewResource()
        {
            CurrentUri = null;
            ContentType = null;
        }

        private ILinkExtractor GuessLinkExtractor(Stream dataStream)
        {
            var ms = new MemoryStream();
            dataStream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);

            if (ContentType.IsUnknownOrNull(ContentType))
                ContentType = ContentType.Guess(ms);

            ms.Seek(0, SeekOrigin.Begin);

            if (ContentType.IsHtmlContentType(ContentType))
                return new HtmlFileLinkExtractor(CurrentUri, ms);
            else if (ContentType.IsXmlContentType(ContentType))
                return new TextFileLinkExtractor(CurrentUri, new StreamReader(ms));
            else if (ContentType.IsTextContentType(ContentType))
                return new TextFileLinkExtractor(CurrentUri, new StreamReader(ms));
            else if (ContentType.IsUnknownOrNull(ContentType))
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
