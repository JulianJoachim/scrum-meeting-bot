This Project made use of the Sample available at https://github.com/OfficeDev/Microsoft-Teams-Samples/tree/main/samples/bot-calling-meeting/csharp

Ein Microsoft Teams Bot um Daily Scrum Meetings in Microsoft Teams zu automatisieren.

Um den Bot in Teams zu benutzen müssen die jeweiligen IDs, Secrets und ngrok URL in launchSettings.json, appsettings.json und manifest.json angepasst werden. Dafür muss ein Azure Bot, eine SQL Datenbank und eine Azure AD App im Azure Portal angelegt werden. Zusätzlich muss der Teams channel aktiv sein und die ngrok URL gesetzt.
Auch die SQL Datenbank Verbindungsdaten müssen im Code angepasst werden.

Der Inhalt des Manifest Ordners muss dann in einer Manifest.zip Datei verpackt werden und in der jeweiligen Teams Organisation hochgeladen und genehmigt werden.

Damit der Bot erreichbar ist, muss das Programm gestartet sein und "ngrok http --host-header=rewrite 3978" ausgeführt worden sein. Die erhaltene ngrok URL muss in den Einstellungen gesetzt werden.
