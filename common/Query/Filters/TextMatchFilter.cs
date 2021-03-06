﻿using System;
using System.IO;
using Fetcho.Common.Entities;

namespace Fetcho.Common
{
    /// <summary>
    /// Simple text match filter to include results
    /// </summary>
    [Filter("NOWAYTOMATCHTHIS", "any_search_term")]
    public class SimpleTextMatchFilter : Filter
    {
        private FastLookupCache<string> seenFragments = new FastLookupCache<string>(1000);

        /// <summary>
        /// Text to match
        /// </summary>
        public string SearchText { get; set; }

        /// <summary>
        /// Name of this filter
        /// </summary>
        public override string Name { get => "TextMatchFilter";  }

        public override decimal Cost => 30m;

        public override bool RequiresTextInput { get => true; }

        public override bool IsReducingFilter => true;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="searchText"></param>
        public SimpleTextMatchFilter(string searchText) 
            => SearchText = searchText;

        /// <summary>
        /// Can't create using the default constructor
        /// </summary>
        private SimpleTextMatchFilter() { }

        /// <summary>
        /// Try and match the fragment from the file
        /// </summary>
        /// <param name="fragment"></param>
        /// <returns></returns>
        public override string[] IsMatch(WorkspaceResult result, string fragment, Stream stream)
        {
            var idx = fragment.IndexOf(SearchText, StringComparison.InvariantCultureIgnoreCase);
            if ( idx > -1 && idx < fragment.Length)
            {
                var frag = fragment.Fragment(idx, 20, SearchText.Length + 20);
                if (seenFragments.Contains(frag))
                {
                    // we've seen this fragment recently, nerf it even if it matches
                    // should get rid of menu links referring to the same link over and over
                    seenFragments.Enqueue(frag);
                    return EmptySet;
                }
                else
                {
                    // we haven't seen this yet
                    seenFragments.Enqueue(frag);
                    return new string[1];
                }
            }
            else
            {
                // no matches
                return EmptySet;
            }
        }

        /// <summary>
        /// Output as string
        /// </summary>
        /// <returns></returns>
        public override string GetQueryText() 
            => string.Format("{0}", SearchText);

        /// <summary>
        /// Parse some text into a TextMatchFilter
        /// </summary>
        /// <param name="queryText"></param>
        /// <returns></returns>
        public static Filter Parse(string queryText, int depth)
            => !string.IsNullOrWhiteSpace(queryText) ? new SimpleTextMatchFilter(queryText) : null;

        public static bool TokenIsFilter(string token)
            => !token.Contains(":");
    }


}
