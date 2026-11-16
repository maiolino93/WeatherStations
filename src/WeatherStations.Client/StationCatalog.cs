using System.Text.Encodings.Web;
using System.Text.Json;

namespace WeatherStations.Client
{
    public static class StationCatalog
    {
       
        private static readonly string[] KnownNames =
        [
            "Hamburg", "Bulawayo", "Palembang", "St. John's", "New York", "Rio De Janeiro", "São Paulo",
            "Zürich", "Århus", "Kraków", "Reykjavík", "Thessaloníki", "Αθήνα", "Москва", "東京",
            "Cape Town", "Dar Es Salaam", "Ho Chi Minh City", "Port-au-Prince", "Winston-Salem",
            "Xi'an", "N'Djamena", "Santa Cruz De La Sierra", "Saint-Étienne", "Frankfurt Am Main",
            "Buenos Aires", "La Paz", "Ciudad De México", "San José", "Bogotá",
            "Kuala Lumpur", "Ulaanbaatar", "Addis Ababa", "Ouagadougou", "Nuku'alofa",
            "Stoke-on-Trent", "Las Palmas De Gran Canaria", "Tel Aviv", "Abu Dhabi", "Wellington",
        ];

        // Every station as the JSON text of its name, in three spellings.
        // The service has to treat all three as the same station.
        public static byte[][] Spellings(int stations)
        {
            var spellings = new byte[stations * 3][];

            for (int i = 0; i < stations; i++)
            {
                string name = i < KnownNames.Length ? KnownNames[i] : $"Station {i + 1:D5}";

                spellings[i * 3] = Encode(name);
                spellings[i * 3 + 1] = Encode(name.ToLowerInvariant());
                spellings[i * 3 + 2] = Encode(name.ToUpperInvariant());
            }

            return spellings;
        }

        private static byte[] Encode(string name)
        {
            return JsonEncodedText.Encode(name, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).EncodedUtf8Bytes.ToArray();
        }
    }
}