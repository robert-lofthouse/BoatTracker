﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Bot.Builder.Luis.Models;

using Newtonsoft.Json.Linq;
using NodaTime.TimeZones;

using BoatTracker.Bot.Configuration;

namespace BoatTracker.Bot.Utils
{
    public static class UserStateExtensions
    {
        public static async Task<string> DescribeReservationsAsync(
            this UserState userState,
            IList<JToken> reservations,
            bool showOwner = false,
            bool showDate = true,
            bool showIndex = false,
            bool useMarkdown = true)
        {
            StringBuilder sb = new StringBuilder();

            int i = 1;
            foreach (var reservation in reservations)
            {
                sb.Append("\n\n");
                sb.Append(await userState.DescribeReservationAsync(reservation, i++, showOwner, showDate, showIndex, useMarkdown));
            }

            if (showIndex)
            {
                if (useMarkdown)
                {
                    sb.AppendFormat("\n\n**{0}**:  **None of the above**", i);
                }
                else
                {
                    sb.AppendFormat("\n\n{0}:  None of the above", i);
                }
            }

            return sb.ToString();
        }

        public static async Task<string> DescribeReservationAsync(
            this UserState userState,
            JToken reservation,
            int index,
            bool showOwner,
            bool showDate,
            bool showIndex,
            bool useMarkdown)
        {
            DateTime startDate = DateTime.Parse(reservation.Value<string>("startDate"));
            startDate = userState.ConvertToLocalTime(startDate);
            var duration = reservation.Value<string>("duration");

            var boatName = await BookedSchedulerCache
                .Instance[userState.ClubId]
                .GetResourceNameFromIdAsync(reservation.Value<long>("resourceId"));

            string owner = string.Empty;

            if (showOwner)
            {
                owner = $" {reservation.Value<string>("firstName")} {reservation.Value<string>("lastName")}";
            }

            if (useMarkdown)
            {
                return string.Format(
                    "{0}**{1} {2}** {3} *({4})*{5}",
                    showIndex ? $"**{index}**:  " : string.Empty,
                    showDate ? startDate.ToLocalTime().ToString("d") : string.Empty,
                    startDate.ToLocalTime().ToString("t"),
                    boatName,
                    duration,
                    owner);
            }
            else
            {
                return string.Format(
                    "{0}{1} {2} {3} ({4}) {5}",
                    showIndex ? $"{index}:  " : string.Empty,
                    showDate ? startDate.ToLocalTime().ToString("d") : string.Empty,
                    startDate.ToLocalTime().ToString("t"),
                    boatName,
                    duration,
                    owner);
            }
        }

        public static async Task<string> SummarizeReservationAsync(this UserState userState, JToken reservation)
        {
            DateTime startDate = DateTime.Parse(reservation.Value<string>("startDate"));
            startDate = userState.ConvertToLocalTime(startDate);

            var boatName = await BookedSchedulerCache
                .Instance[userState.ClubId]
                .GetResourceNameFromIdAsync(reservation.Value<long>("resourceId"));

            return string.Format(
                "{0} {1} {2}",
                startDate.ToLocalTime().ToString("d"),
                startDate.ToLocalTime().ToString("t"),
                boatName
                );
        }

        /// <summary>
        /// Look for a matching boat resource given a user-entered name string.
        /// </summary>
        /// <param name="userState">The user context</param>
        /// <param name="name">The boat name as typed by the user</param>
        /// <returns>The JToken for the matching boat resource, or null if no good match was found.</returns>
        public static Task<JToken> FindBestResourceMatchAsync(this UserState userState, string name)
        {
            return FindBestResourceMatchAsync(
                userState,
                new List<EntityRecommendation>()
                {
                    new EntityRecommendation
                    {
                        Type = "boatName",
                        Entity = name
                    }
                });
        }

        /// <summary>
        /// Look for an acceptable match between the 'boatName' entities found by LUIS and
        /// the known set of boat names for the user's club. Consider alternate names for
        /// each boat as configured by the administrator using a custom attribute.
        /// </summary>
        /// <param name="userState">The user context</param>
        /// <param name="entities">The entities discovered by LUIS</param>
        /// <returns>The JToken for the matching boat resource, or null if no good match was found.</returns>
        public static async Task<JToken> FindBestResourceMatchAsync(this UserState userState, IList<EntityRecommendation> entities)
        {
            var entityWords = entities.Where(e => e.Type == "boatName").SelectMany(e => e.Entity.ToLower().Split(' ')).ToList();

            if (entityWords.Count == 0)
            {
                return null;
            }

            var resources = await BookedSchedulerCache.Instance[userState.ClubId].GetResourcesAsync();

            var boat = resources.FirstOrDefault((b) => PerfectMatchBoat(entityWords, b));

            if (boat != null)
            {
                return boat;
            }

            //
            // Next, check to see if a subset of the entities completely spans the boat name words.
            // This could happen if LUIS classifies "extra" words as being part of the boat name.
            //
            boat = resources.FirstOrDefault((b) => OverMatchBoat(entityWords, b));

            if (boat != null)
            {
                return boat;
            }

            //
            // Next, check for cases where the entities all match a subset of the boat name words.
            // This could happen if LUIS failed to classify some words as being part of the boat
            // name OR if the user provides only a portion of a longer name.
            //
            var underMatches = resources.Where((b) => UnderMatchBoat(entityWords, b));

            // TODO: If one partial match is superior to all others, we should allow it.

            if (underMatches.Count() > 1)
            {
                return null; // The name partially matches multiple boats so return a failure.
            }

            return underMatches.FirstOrDefault();
        }

        private static bool PerfectMatchBoat(IList<string> entityWords, JToken boat)
        {
            //
            // Check for a perfect match with any of the boat names
            //
            return GetBoatNames(boat).Any(name => PerfectMatchName(entityWords, name.ToLower().Split(' ')));
        }

        private static bool PerfectMatchName(IList<string> entityWords, string[] boatNameWords)
        {
            return entityWords.Count == boatNameWords.Count() && boatNameWords.All(word => entityWords.Contains(word));
        }

        private static bool OverMatchBoat(IList<string> entityWords, JToken boat)
        {
            //
            // Check to see if a subset of the entities completely spans the boat name words.
            // This could happen if LUIS classifies "extra" words as being part of the boat name.
            //
            return GetBoatNames(boat).Any(name => OverMatchName(entityWords, name.ToLower().Split(' ')));
        }

        private static bool OverMatchName(IList<string> entityWords, string[] boatNameWords)
        {
            return entityWords.Count > boatNameWords.Count() && boatNameWords.All(word => entityWords.Contains(word));
        }

        private static bool UnderMatchBoat(IList<string> entityWords, JToken boat)
        {
            //
            // Check for cases where the entities all match a subset of the boat name words.
            // This could happen if LUIS failed to classify some words as being part of the boat
            // name OR if the user provides only a portion of a longer name.
            //
            return GetBoatNames(boat).Any(name => UnderMatchName(entityWords, name.ToLower().Split(' ')));
        }

        private static bool UnderMatchName(IList<string> entityWords, string[] boatNameWords)
        {
            return entityWords.Count < boatNameWords.Count() && entityWords.All(word => boatNameWords.Contains(word));
        }

        private static IList<string> GetBoatNames(JToken boat)
        {
            // Start with the set of alternate names (if any) for the boat
            var boatNames = (boat
                .Value<JArray>("customAttributes")
                .Where(x => x.Value<string>("label") == "Alternate names")
                .First()
                .Value<string>("value") ?? string.Empty)
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            // And add its preferred name
            boatNames.Add(boat.Value<string>("name"));

            return boatNames;
        }

        public static DateTime ConvertToLocalTime(this UserState userState, DateTime dateTime)
        {
            return dateTime + userState.LocalOffsetForDate(dateTime);
        }

        public static TimeSpan LocalOffsetForDate(this UserState userState, DateTime date)
        {
            var mappings = TzdbDateTimeZoneSource.Default.WindowsMapping.MapZones;
            var mappedTz = mappings.FirstOrDefault(x => x.TzdbIds.Any(z => z.Equals(userState.Timezone, StringComparison.OrdinalIgnoreCase)));

            if (mappedTz == null)
            {
                return TimeSpan.Zero;
            }

            var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(mappedTz.WindowsId);

            TimeSpan offset = tzInfo.GetUtcOffset(date.Date);

            return offset;
        }
    }
}