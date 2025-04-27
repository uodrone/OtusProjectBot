using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HRProBot.Controllers;

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

        public async Task SendMediaGroupWithCaption(long chatId, CancellationToken cancellationToken, List<InputMediaPhoto> photos, string caption, ReplyKeyboardMarkup? buttons)
        {
            try
            {
                int maxCaptionLength = 1024;
                if (string.IsNullOrEmpty(caption) || caption.Length <= maxCaptionLength)
                {
                    photos[0] = new InputMediaPhoto(photos[0].Media) { Caption = caption, ParseMode = ParseMode.Html };
                    await _botClient.SendMediaGroupAsync(chatId: chatId, media: photos, cancellationToken: cancellationToken);
                    return;
                }

                int splitPosition = FindSplitPosition(caption, maxCaptionLength);
                if (splitPosition == -1)
                {
                    splitPosition = maxCaptionLength;
                }

                string photoCaption = caption.Substring(0, splitPosition).Trim();
                string remainingText = caption.Substring(splitPosition).Trim();

                photos[0].Caption = photoCaption;
                await _botClient.SendMediaGroupAsync(chatId: chatId, media: photos, cancellationToken: cancellationToken);

                if (!string.IsNullOrEmpty(remainingText))
                {
                    await SendMessage(chatId, cancellationToken, remainingText, buttons);
                }
            }
            catch (Exception ex)
            {
                ///<todo>Удолить перед публикаций на прод</todo>
                await SendMessage(chatId, cancellationToken, "Не удалось отправить.", null);
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
    }
}