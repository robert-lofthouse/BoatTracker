﻿using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BoatTracker.Bot.DataObjects
{
    public class RfidEvent
    {
        public string EPC { get; set; }

        public DateTime? ReadTime { get; set; }

        public string Direction { get; set; }

        public string Location { get; set; }

        public string ReadZone { get; set; }

        [JsonExtensionData(ReadData=true)]
        public IDictionary<string, JToken> Extras { get; }
    }
}
