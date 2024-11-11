using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Linq;

class Program
{
    // CardDAV server details
    const string CARD_DAV_URL = "https://www.google.com/carddav/v1/principals/xxxxxxxxx@gmail.com/lists/default/";
    const string USERNAME = "xxxxxxxxx@gmail.com";
    // You must enable 2FA and create Application Password. Use it to access CardDAV server. Do not use your main Google account password
    const string PASSWORD = "16-digit application password";
    const string OUTPUT_DIR = @"c:\temp\google_contacts";
    const string COMBINED_FILE = OUTPUT_DIR + "\\contacts_combined.vcf";

    // Custom PROPFIND HttpMethod
    static readonly HttpMethod PropFind = new HttpMethod("PROPFIND");

    // Main entry point
    static async Task Main(string[] args)
    {
        // Ensure output directory exists
        if (!Directory.Exists(OUTPUT_DIR))
        {
            Directory.CreateDirectory(OUTPUT_DIR);
        }

        // Fetch contact list
        List<string> contacts = await FetchContactsList();
        Console.WriteLine($"Found {contacts.Count} contacts.");

        // Open combined file for writing
        using (StreamWriter combinedFile = new StreamWriter(COMBINED_FILE, false))
        {
            foreach (string href in contacts)
            {
                await FetchAndSaveContact(href, combinedFile);
            }
        }

        Console.WriteLine($"All contacts saved to: {COMBINED_FILE}");
    }

    // Fetch contact list
    static async Task<List<string>> FetchContactsList()
    {
        string requestBody = @"<?xml version=""1.0"" encoding=""UTF-8""?>
    <d:propfind xmlns:d=""DAV:"" xmlns:card=""urn:ietf:params:xml:ns:carddav"">
        <d:prop>
            <d:getetag/>
        </d:prop>
    </d:propfind>";

        using (HttpClient client = CreateHttpClient())
        {
            HttpRequestMessage request = new HttpRequestMessage(PropFind, CARD_DAV_URL)
            {
                Content = new StringContent(requestBody)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            request.Headers.Add("Depth", "1");

            HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to fetch contacts list: {response.StatusCode}");
                string errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine(errorContent);
                return new List<string>();
            }

            string responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine("Raw XML Response:\n" + responseContent);

            // Parse XML and extract hrefs
            List<string> hrefs = new List<string>();
            XDocument doc = XDocument.Parse(responseContent);

            XNamespace dav = "DAV:";
            foreach (XElement responseElement in doc.Descendants(dav + "response"))
            {
                string hrefValue = responseElement.Element(dav + "href")?.Value;

                if (!string.IsNullOrEmpty(hrefValue))
                {
                    bool hasValidPropstat = false;

                    // Check for at least one valid 200 OK propstat
                    foreach (XElement propstatElement in responseElement.Elements(dav + "propstat"))
                    {
                        XElement statusElement = propstatElement.Element(dav + "status");
                        if (statusElement != null && statusElement.Value.Contains("HTTP/1.1 200 OK"))
                        {
                            hasValidPropstat = true;
                            break; // Found a valid propstat, no need to check further
                        }
                    }

                    if (hasValidPropstat)
                    {
                        hrefs.Add(hrefValue);
                        Console.WriteLine($"Valid contact URL found: {hrefValue}");
                    }
                }
            }

            return hrefs;
        }
    }


    // Fetch and save contact
    static async Task FetchAndSaveContact(string href, StreamWriter combinedFile)
    {
        string contactUrl = "https://www.google.com" + href;

        using (HttpClient client = CreateHttpClient())
        {
            HttpResponseMessage response = await client.GetAsync(contactUrl);

            if (response.IsSuccessStatusCode)
            {
                string vcardContent = await response.Content.ReadAsStringAsync();
                string cleanedContent = CleanVCard(vcardContent);

                // Write the contact to the combined file
                await combinedFile.WriteLineAsync(cleanedContent);
                await combinedFile.WriteLineAsync(); // Separate entries with a blank line
                Console.WriteLine($"Fetched and saved contact: {href}");
            }
            else
            {
                Console.WriteLine($"Failed to fetch contact: {href} ({response.StatusCode})");
                string errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine(errorContent);
            }
        }
    }

    // Create HTTP client
    static HttpClient CreateHttpClient()
    {
        HttpClientHandler handler = new HttpClientHandler
        {
            Credentials = new System.Net.NetworkCredential(USERNAME, PASSWORD)
        };

        return new HttpClient(handler);
    }

    // Clean vCard content
    static string CleanVCard(string content)
    {
        return content.Replace("\r", "").Trim();
    }
}
