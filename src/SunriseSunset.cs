﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using GeoTimeZone;
using SunCalcNet.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TimeZoneConverter;

namespace WinDynamicDesktop
{
    public enum PolarPeriod { None, PolarDay, PolarNight, CivilPolarDay, CivilPolarNight }

    public class SolarData
    {
        public PolarPeriod polarPeriod = PolarPeriod.None;
        public DateTime sunriseTime { get; set; }
        public DateTime sunsetTime { get; set; }
        public DateTime[] solarTimes { get; set; }
        public DateTime solarNoon { get; set; }

        public bool IsPolarPeriodTotal()
        {
            return polarPeriod == PolarPeriod.PolarDay || polarPeriod == PolarPeriod.PolarNight;
        }
    }

    class SunriseSunsetService
    {
        private static readonly Func<string, string> _ = Localization.GetTranslation;
        internal static Func<DateTime> GetDateTimeNow = () => DateTime.Now;

        private static SolarData GetUserProvidedSolarData(DateTime date)
        {
            SolarData data = new SolarData();
            data.sunriseTime = date.Date + DateTime.Parse(JsonConfig.settings.sunriseTime,
                CultureInfo.InvariantCulture).TimeOfDay;
            data.sunsetTime = date.Date + DateTime.Parse(JsonConfig.settings.sunsetTime,
                CultureInfo.InvariantCulture).TimeOfDay;

            int halfSunriseSunsetDuration = JsonConfig.settings.sunriseSunsetDuration * 30;
            data.solarTimes = new DateTime[4]
            {
                data.sunriseTime.AddSeconds(-halfSunriseSunsetDuration),
                data.sunriseTime.AddSeconds(halfSunriseSunsetDuration),
                data.sunsetTime.AddSeconds(-halfSunriseSunsetDuration),
                data.sunsetTime.AddSeconds(halfSunriseSunsetDuration)
            };

            return data;
        }

        private static List<SunPhase> GetSunPhases(DateTime date, double latitude, double longitude)
        {
            string tzName = TimeZoneLookup.GetTimeZone(latitude, longitude).Result;
            TimeZoneInfo tzInfo = TZConvert.GetTimeZoneInfo(tzName);
            DateTime localDate = date.Add(GetDateTimeNow().TimeOfDay);
            if (tzInfo.IsInvalidTime(localDate))
            {
                TimeSpan offsetBefore = tzInfo.GetUtcOffset(localDate.AddDays(-1));
                TimeSpan offsetAfter = tzInfo.GetUtcOffset(localDate.AddDays(1));
                localDate = localDate.Add(offsetAfter - offsetBefore);
            }
            DateTime tzDate = TimeZoneInfo.ConvertTime(localDate, tzInfo);
            // Set time to noon because of https://github.com/mourner/suncalc/issues/107
            DateTime utcDate = new DateTimeOffset(
                tzDate.Year, tzDate.Month, tzDate.Day, 12, 0, 0, tzInfo.GetUtcOffset(tzDate)).UtcDateTime;
            return SunCalcNet.SunCalc.GetSunPhases(utcDate, latitude, longitude).ToList();
        }

        private static DateTime GetSolarTime(List<SunPhase> sunPhases, SunPhaseName desiredPhase)
        {
            SunPhase sunPhase = sunPhases.FirstOrDefault(sp => sp.Name.Value == desiredPhase.Value);
            return sunPhase.Name != null ? sunPhase.PhaseTime.ToLocalTime() : DateTime.MinValue;
        }

        public static SolarData GetSolarData(DateTime date)
        {
            if (JsonConfig.settings.locationMode < 0)
            {
                return GetUserProvidedSolarData(date);
            }

            double latitude = JsonConfig.settings.latitude.Value;
            double longitude = JsonConfig.settings.longitude.Value;
            var sunPhases = GetSunPhases(date, latitude, longitude);
            SolarData data = new SolarData();

            // Sunrise/sunset = -0.833 deg, Civil twilight = -6 deg, Golden hour = +6 deg above horizon
            data.sunriseTime = GetSolarTime(sunPhases, SunPhaseName.Sunrise);
            data.sunsetTime = GetSolarTime(sunPhases, SunPhaseName.Sunset);
            data.solarTimes = new DateTime[4]
            {
                GetSolarTime(sunPhases, SunPhaseName.Dawn),
                GetSolarTime(sunPhases, SunPhaseName.GoldenHourEnd),
                GetSolarTime(sunPhases, SunPhaseName.GoldenHour),
                GetSolarTime(sunPhases, SunPhaseName.Dusk)
            };
            data.solarNoon = GetSolarTime(sunPhases, SunPhaseName.SolarNoon);

            // Assume polar day/night if sunrise/sunset time are undefined
            if (data.sunriseTime == DateTime.MinValue || data.sunsetTime == DateTime.MinValue)
            {
                double sunAltitude = SunCalcNet.SunCalc.GetSunPosition(data.solarNoon.ToUniversalTime(), latitude,
                    longitude).Altitude;
                data.polarPeriod = sunAltitude > 0 ? PolarPeriod.PolarDay : PolarPeriod.PolarNight;
            }
            // Skip night segment (civil polar day) if dawn/dusk time are undefined
            else if (data.solarTimes[0] == DateTime.MinValue && data.solarTimes[3] == DateTime.MinValue)
            {
                data.solarTimes[0] = data.solarNoon.AddHours(-12);
                data.solarTimes[3] = data.solarNoon.AddHours(12).AddTicks(-1);
                data.polarPeriod = PolarPeriod.CivilPolarDay;
            }
            // Skip day segment (civil polar night) if golden hour is undefined
            else if (data.solarTimes[1] == DateTime.MinValue && data.solarTimes[2] == DateTime.MinValue)
            {
                data.solarTimes[1] = data.solarTimes[2] = data.solarNoon;
                data.polarPeriod = PolarPeriod.CivilPolarNight;
            }

            return data;
        }

        public static string GetSunriseSunsetString(SolarData solarData)
        {
            switch (solarData.polarPeriod)
            {
                case PolarPeriod.PolarDay:
                    return _("Sunrise/Sunset: Always up");
                case PolarPeriod.PolarNight:
                    return _("Sunrise/Sunset: Always down");
                default:
                    return string.Format(_("Sunrise: {0}, Sunset: {1}"), solarData.sunriseTime.ToShortTimeString(),
                        solarData.sunsetTime.ToShortTimeString());
            }
        }
    }
}
