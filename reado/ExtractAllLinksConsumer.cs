﻿using BracketPipe;
using Fetcho.Common;
using System;
using System.IO;

namespace Fetcho
{
    public class ExtractAllLinksConsumer : WebDataPacketConsumer
    {
        public Uri CurrentUri;
        public ContentType ContentType;

        public override string Name { get => "Extract All Links Experiment"; }
        public override bool ProcessesRequest { get => true; }
        public override bool ProcessesResponse { get => true; }

        public override void ProcessRequest(string request)
        {
            CurrentUri = WebDataPacketReader.GetUriFromRequestString(request);
        }

        public override void ProcessResponseHeaders(string responseHeaders)
        {
            ContentType = WebDataPacketReader.GetContentTypeFromResponseHeaders(responseHeaders);
        }

        public override void ProcessResponseStream(Stream dataStream)
        {
            if (dataStream == null) return;
            var ms = new MemoryStream();
            dataStream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            if (ContentType.IsUnknownOrNull(ContentType))
                ContentType = ContentType.Guess(ms);

            ms.Seek(0, SeekOrigin.Begin);

            if (ContentType.IsUnknownOrNull(ContentType) || ContentType.MediaType == "text")
            {
                using (var p = new HtmlReader(ms))
                {
                    while ( !p.EOF)
                    {
                        var node = p.NextNode();

                        if ( node.Type == HtmlTokenType.StartTag)
                            if (node.Value == "script")
                            {
                                string src = p.GetAttribute("src");
                                if ( !String.IsNullOrWhiteSpace(src))
                                    Console.WriteLine(src);
                            }

                    }
                }
            }
        }

        public override void NewResource()
        {
            CurrentUri = null;
        }
    }
}
