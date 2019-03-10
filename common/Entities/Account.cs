﻿using System;
using System.Collections.Generic;

namespace Fetcho.Common.Entities
{
    public class Account
    {
        public const int MinKeyLength = 12;

        public string Name { get; set; }

        public DateTime Created { get; set; }

        public bool IsActive { get; set; }

        public List<AccessKey> AccessKeys { get; set; }


        public Account()
        {
            Created = DateTime.Now;
            IsActive = true;
            Name = Utility.GetRandomHashString();
            AccessKeys = new List<AccessKey>();
        }

        public static bool IsValid(Account key)
        {
            if (String.IsNullOrWhiteSpace(key.Name))
                return false;
            if (key.Name.Length < MinKeyLength)
                return false;
            return true;
        }

        public static void Validate(Account key)
        {
            if (String.IsNullOrWhiteSpace(key.Name))
                throw new InvalidObjectFetchoException("No key set");
            if (key.Name.Length < 12)
                throw new InvalidObjectFetchoException("Key too short");
            if (!Account.IsValid(key))
                throw new InvalidObjectFetchoException("Object is invalid");
        }
    }
}
