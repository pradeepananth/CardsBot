﻿using AdaptiveCards;
using AdaptiveCards.Templating;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Schema.Teams;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SearchCommand.Model;
using System.Collections.Specialized;
using System.Security.Principal;
using System.Web;

namespace SearchCommand
{
    /// <summary>
    /// Defines the activity handlers.
    /// </summary>
    public class ActivityHandlers
    {
        private readonly HttpClient _httpClient;
        private static string _packageCardFilePath = Path.Combine(".", "Resources", "PackageCard.json");
        private static string _designerCardFilePath = Path.Combine(".", "Resources", "DesignerCard.json");

        public ActivityHandlers(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient("WebClient");
        }

        /// <summary>
        /// Handles Message Extension query events.
        /// </summary>
        public QueryHandlerAsync<TurnState> QueryHandler => async (ITurnContext turnContext, TurnState turnState, Query<Dictionary<string, object>> query, CancellationToken cancellationToken) =>
        {
            string text = (string)query.Parameters["queryText"];
            int count = query.Count;

            Package[] packages = await SearchPackages(text, count, cancellationToken);

            // Format search results
            List<MessagingExtensionAttachment> attachments = packages.Select(package => new MessagingExtensionAttachment
            {
                ContentType = HeroCard.ContentType,
                Content = new HeroCard
                {
                    Title = package.Id,
                    Text = package.Description
                },
                Preview = new HeroCard
                {
                    Title = package.Id,
                    Text = package.Description,
                    Tap = new CardAction
                    {
                        Type = "invoke",
                        Value = package
                    }
                }.ToAttachment()
            }).ToList();

            return new MessagingExtensionResult
            {
                Type = "result",
                AttachmentLayout = "list",
                Attachments = attachments
            };
        };

        /// <summary>
        /// Handles Message Extension selecting item events.
        /// </summary>
        public SelectItemHandlerAsync<TurnState> SelectItemHandler => async (ITurnContext turnContext, TurnState turnState, object item, CancellationToken cancellationToken) =>
        {
            JObject? obj = item as JObject;
            CardPackage package = CardPackage.Create(obj!.ToObject<Package>()!);
            string cardTemplate = await File.ReadAllTextAsync(_packageCardFilePath, cancellationToken)!;
            string cardContent = new AdaptiveCardTemplate(cardTemplate).Expand(package);
            MessagingExtensionAttachment attachment = new()
            {
                ContentType = AdaptiveCard.ContentType,
                Content = JsonConvert.DeserializeObject(cardContent)
            };

            return new MessagingExtensionResult
            {
                Type = "result",
                AttachmentLayout = "list",
                Attachments = new List<MessagingExtensionAttachment> { attachment }
            };
        };

        public RouteHandler<TurnState> MessageHandler = async (ITurnContext turnContext, TurnState turnState, CancellationToken cancellationToken) =>
        {
            turnContext.Activity.Text = turnContext.Activity.RemoveRecipientMention();
            IMessageActivity message;
            switch (turnContext.Activity.Text)
            {
                case "designer":
                    string cardTemplate = await File.ReadAllTextAsync(_designerCardFilePath, cancellationToken)!;
                    string cardContent = new AdaptiveCardTemplate(cardTemplate).Expand(new
                    {
                        PromptText = turnContext.Activity.Text
                    });
                    message = await SendAdaptiveCard(turnContext, cardContent, cancellationToken);
                    break;
                case "nugetjsoncard":
                    string text = "Json";
                    Package[] packages = await SearchPackages(text, 1, cancellationToken);
                    string cardTemplateJson = await File.ReadAllTextAsync(_packageCardFilePath, cancellationToken)!;
                    string cardContentJson = new AdaptiveCardTemplate(cardTemplateJson).Expand(packages[0]);
                    message = await SendAdaptiveCard(turnContext, cardContentJson, cancellationToken);
                    break;
                default:
                    await turnContext.SendActivityAsync($"Your prompt {turnContext.Activity.Text} did not match any actions!", cancellationToken: cancellationToken);
                    break;
            }
        };

        private static async Task<IMessageActivity> SendAdaptiveCard(ITurnContext turnContext, string cardContent, CancellationToken cancellationToken)
        {
            IMessageActivity message;
            var adaptiveCardAttachment = new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(cardContent),
            };
            message = MessageFactory.Attachment(adaptiveCardAttachment);
            AIEntity aiEntity = new();
            message.Entities = new[] { aiEntity };
            await turnContext.SendActivityAsync(message!, cancellationToken: cancellationToken);
            return message;
        }

        private static async Task<Package[]> SearchPackages(string text, int size, CancellationToken cancellationToken)
        {
            // Call NuGet Search API
            NameValueCollection query = HttpUtility.ParseQueryString(string.Empty);
            query["q"] = text;
            query["take"] = size.ToString();
            string queryString = query.ToString()!;
            string responseContent;
            try
            {
                responseContent = await _httpClient.GetStringAsync($"https://azuresearch-usnc.nuget.org/query?{queryString}", cancellationToken);
            }
            catch (Exception)
            {
                throw;
            }

            if (!string.IsNullOrWhiteSpace(responseContent))
            {
                JObject responseObj = JObject.Parse(responseContent);
                return responseObj["data"]?
                    .Select(obj => obj.ToObject<Package>()!)?
                    .ToArray() ?? Array.Empty<Package>();
            }
            else
            {
                return Array.Empty<Package>();
            }
        }
    }
}
