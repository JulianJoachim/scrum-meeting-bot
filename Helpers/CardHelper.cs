// <copyright file="CardHelper.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace CallingBotSample.Helpers
{
    using CallingBotSample.Interfaces;
    using Microsoft.Bot.Schema;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using System.IO;

    /// <summary>
    /// Helper for cards.
    /// </summary>
    public class CardHelper : ICard
    {
        private readonly ILogger<CardHelper> logger;

        public CardHelper(ILogger<CardHelper> logger)
        {
            this.logger = logger;
        }

        public Attachment GetWelcomeCardAttachment()
        {
            var welcomeCardAttachment = new Attachment();
            try
            {
                string[] welcomeCardPaths = { ".", "Resources", "WelcomeCard.json" };
                var welcomeCardString = File.ReadAllText(Path.Combine(welcomeCardPaths));
                welcomeCardAttachment.ContentType = "application/vnd.microsoft.card.adaptive";
                welcomeCardAttachment.Content = JsonConvert.DeserializeObject(welcomeCardString);
            }
            catch (System.Exception ex)
            {
                this.logger.LogError(ex, ex.Message);
            }

            return welcomeCardAttachment;
        }

        public Attachment GetInfoCardAttachment()
        {
            var infoCardAttachment = new Attachment();
            try
            {
                string[] infoCardPaths = { ".", "Resources", "InfoCard.json" };
                var infoCardString = File.ReadAllText(Path.Combine(infoCardPaths));
                infoCardAttachment.ContentType = "application/vnd.microsoft.card.adaptive";
                infoCardAttachment.Content = JsonConvert.DeserializeObject(infoCardString);
            }
            catch (System.Exception ex)
            {
                this.logger.LogError(ex, ex.Message);
            }

            return infoCardAttachment;
        }

        public Attachment GetReportCardAttachment()
        {
            var reportCardAttachment = new Attachment();
            try
            {
                string[] reportCardPaths = { ".", "Resources", "ReportCard.json" };
                var reportCardString = File.ReadAllText(Path.Combine(reportCardPaths));
                reportCardAttachment.ContentType = "application/vnd.microsoft.card.adaptive";
                reportCardAttachment.Content = JsonConvert.DeserializeObject(reportCardString);
            }
            catch (System.Exception ex)
            {
                this.logger.LogError(ex, ex.Message);
            }

            return reportCardAttachment;
        }
    }
}