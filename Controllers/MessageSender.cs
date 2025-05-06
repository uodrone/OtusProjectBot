using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HRProBot.Controllers;
using System.Text.RegularExpressions;

namespace HRProBot.Services
{
    public class MessageSender
    {
        private static ITelegramBotClient _botClient;

        public MessageSender(ITelegramBotClient botClient)
        {
            _botClient = botClient;
        }

        private int FindSplitPosition(string text, int maxLength)
        {
            int newlinePosition = text.LastIndexOf('\n', maxLength - 1);
            if (newlinePosition != -1)
            {
                return newlinePosition;
            }
            int dotPosition = text.LastIndexOf('.', maxLength - 1);
            if (dotPosition != -1)
            {
                return dotPosition;
            }
            return maxLength;
        }

        /// <summary>
        /// Отправляет видеосообщение (кружок).
        /// </summary>
        public async Task SendVideoNote(long chatId, CancellationToken cancellationToken, string fileIdOrUrl, ReplyKeyboardMarkup? buttons)
        {
            try
            {
                string filePath = null;

                // Если это URL, сохраняем файл локально
                if (Uri.TryCreate(fileIdOrUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    var FileController = new FileController();
                    filePath = await FileController.SaveFileFromUrl(fileIdOrUrl, "VideoNotes");
                }
                else
                {
                    // Если это file_id или локальный путь, используем его напрямую
                    filePath = fileIdOrUrl;
                }

                // Отправляем видеосообщение
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    await _botClient.SendVideoNoteAsync(
                        chatId: chatId,
                        videoNote: InputFile.FromStream(fileStream),
                        replyMarkup: buttons,
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                ///<todo>Удолить перед публикаций на прод</todo>
                await SendMessage(chatId, cancellationToken, $"Видео не может быть отправлено: {ex.Message}", buttons);
            }
        }

        public async Task SendVideoWithCaption(long chatId, CancellationToken cancellationToken, string fileIdOrUrl, string caption, ReplyKeyboardMarkup? buttons)
        {
            try
            {
                await _botClient.SendVideoAsync(
                    chatId: chatId,
                    video: fileIdOrUrl,
                    caption: caption,
                    replyMarkup: buttons,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                ///<todo>Удолить перед публикаций на прод</todo>
                await SendMessage(chatId, cancellationToken, $"Видео не может быть отправлено: {ex.Message}", buttons);
            }
        }

        /// <summary>
        /// Отправляет медиагруппу с подписью. Если подпись содержит HTML или длиннее 1024 символов — отправляется отдельным сообщением.
        /// </summary>
        /// <summary>
        /// Отправляет медиагруппу с подписью.
        /// Если подпись содержит HTML-теги или длиннее 1024 символов — отправляется отдельным сообщением.
        /// </summary>
        public async Task SendMediaGroupWithCaption(
            long chatId,
            CancellationToken cancellationToken,
            List<InputMediaPhoto> photos,
            string caption,
            ReplyKeyboardMarkup? buttons)
        {
            try
            {
                if (photos == null || photos.Count == 0)
                {
                    return;
                }

                bool shouldSendMessageSeparately = false;

                // Проверяем: есть ли в строке HTML-теги
                var htmlTagRegex = new Regex("<.*?>");
                if (htmlTagRegex.IsMatch(caption))
                {
                    shouldSendMessageSeparately = true;
                }
                else if (!string.IsNullOrEmpty(caption) && caption.Length > 1024)
                {
                    shouldSendMessageSeparately = true;
                }

                if (shouldSendMessageSeparately)
                {
                    // Отправляем медиагруппу без подписи
                    await _botClient.SendMediaGroupAsync(
                    chatId: chatId,
                    media: photos,
                    cancellationToken: cancellationToken);

                    // Отправляем текст отдельным сообщением с ParseMode.Html
                    if (!string.IsNullOrEmpty(caption))
                    {
                        await SendMessage(chatId, cancellationToken, caption, null);
                    }

                    // Кнопка в конце
                    await SendMessage(chatId, cancellationToken, "Чтобы перейти к нужному разделу, нажми кнопку в меню 🔽", buttons);
                }
                else
                {
                    // Просто ставим короткую подпись на первую фотографию
                    if (!string.IsNullOrEmpty(caption))
                    {
                        photos[0] = new InputMediaPhoto(photos[0].Media)
                        {
                            Caption = caption,
                            ParseMode = ParseMode.Html // Не влияет, но не мешает
                        };
                    }

                    await _botClient.SendMediaGroupAsync(
                    chatId: chatId,
                    media: photos,
                    cancellationToken: cancellationToken);

                    if (buttons != null)
                    {
                        await SendMessage(chatId, cancellationToken, "Чтобы перейти к нужному разделу, нажми кнопку в меню 🔽", buttons);
                    }
                }
            }
            catch (Exception ex)
            {
                ///<todo>Удолить перед публикаций на прод</todo>
                await SendMessage(chatId, cancellationToken, "Не удалось отправить медиагруппу.", null);
            }
        }

        public async Task SendPhotoWithCaption(long chatId, CancellationToken cancellationToken, string fileId, string caption, ReplyKeyboardMarkup? buttons)
        {
            try
            {
                int maxCaptionLength = 1024;
                if (string.IsNullOrEmpty(caption) || caption.Length <= maxCaptionLength)
                {
                    await _botClient.SendPhotoAsync(
                        chatId: chatId,
                        photo: fileId,
                        caption: caption,
                        parseMode: ParseMode.Html,
                        replyMarkup: buttons,
                        cancellationToken: cancellationToken);
                    return;
                }

                int splitPosition = FindSplitPosition(caption, maxCaptionLength);
                if (splitPosition == -1)
                {
                    splitPosition = maxCaptionLength;
                }

                string photoCaption = caption.Substring(0, splitPosition).Trim();
                string remainingText = caption.Substring(splitPosition).Trim();

                await _botClient.SendPhotoAsync(
                    chatId: chatId,
                    photo: fileId,
                    caption: photoCaption,
                    parseMode: ParseMode.Html,
                    replyMarkup: buttons,
                    cancellationToken: cancellationToken);

                if (!string.IsNullOrEmpty(remainingText))
                {
                    await SendMessage(chatId, cancellationToken, remainingText, buttons);
                }
            }
            catch (Exception ex)
            {
                ///<todo>Удолить перед публикаций на прод</todo>
                await SendMessage(chatId, cancellationToken, "Не удалось отправить изображение.", buttons);
            }
        }

        public async Task SendMessage(long chatId, CancellationToken cancellationToken, string textMessage, ReplyKeyboardMarkup? buttons)
        {
            ReplyKeyboardRemove removeKeyboard = new ReplyKeyboardRemove();
            if (buttons == null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: textMessage,
                    parseMode: ParseMode.Html,
                    replyMarkup: removeKeyboard,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: textMessage,
                    parseMode: ParseMode.Html,
                    replyMarkup: buttons,
                    cancellationToken: cancellationToken);
            }
        }

        public async Task MailMessage(string message, string imagesUrl, List<InputMediaPhoto> mediaGroup, string videoUrl, string videoNoteUrl, CancellationToken cancellationToken, ReplyKeyboardMarkup buttons, long chatId)
        {
            if (!string.IsNullOrEmpty(videoNoteUrl))
            {
                if (!string.IsNullOrEmpty(message))
                {
                    await SendMessage(chatId, cancellationToken, message, buttons);
                }
                await SendVideoNote(chatId, cancellationToken, videoNoteUrl, buttons);
            }
            else if (!string.IsNullOrEmpty(videoUrl))
            {
                await SendVideoWithCaption(chatId, cancellationToken, videoUrl, message, buttons);
            }
            else if (!string.IsNullOrEmpty(imagesUrl))
            {
                if (mediaGroup.Count == 1)
                {
                    await SendPhotoWithCaption(chatId, cancellationToken, imagesUrl, message, buttons);
                }
                else if (mediaGroup.Count > 1)
                {
                    await SendMediaGroupWithCaption(chatId, cancellationToken, mediaGroup, message, buttons);
                }
            }
            else
            {
                await SendMessage(chatId, cancellationToken, message, buttons);
            }
        }

        public async Task<List<InputMediaPhoto>> ConvertImgStringToMediaListAsync(string imagesUrl)
        {
            // Разделяем строку с URL изображений, если она не пустая
            string[] imageArray = !string.IsNullOrEmpty(imagesUrl) ? imagesUrl.Split(';') : Array.Empty<string>();
            var mediaGroup = new List<InputMediaPhoto>();

            foreach (var url in imageArray)
            {
                // Добавляем каждую фотографию в медиагруппу, используя URL
                mediaGroup.Add(new InputMediaPhoto(url));
            }

            return mediaGroup;
        }

        public static string ConvertHtmlToTelegramMarkdown(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            // Замена основных тегов на MarkdownV2
            html = html.Replace("<b>", "*")
                       .Replace("</b>", "*")
                       .Replace("<strong>", "*")
                       .Replace("</strong>", "*")
                       .Replace("<i>", "_")
                       .Replace("</i>", "_")
                       .Replace("<em>", "_")
                       .Replace("</em>", "_");

            // Замена ссылок: <a href="url">text</a> => [text](url)
            var linkRegex = new Regex("<a\\s+href=[\"'](?<url>[^\"']*)[\"']>(?<text>[^<]*)</a>",
                                     RegexOptions.IgnoreCase);
            html = linkRegex.Replace(html, "[$2]($1)");

            // Удаление неподдерживаемых тегов
            html = Regex.Replace(html, "<(?!\\/?(b|i|strong|em|a)\\b)[^>]*>", "", RegexOptions.IgnoreCase);

            // Экранирование специальных символов MarkdownV2
            var specialChars = new[] { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
            foreach (var c in specialChars)
            {
                html = html.Replace(c.ToString(), $"\\{c}");
            }

            return html;
        }
    }
}