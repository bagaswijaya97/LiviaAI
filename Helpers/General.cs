using System.IO.Compression;
using System.Text;

namespace GeminiAIServices.Helpers
{
    public class General { }

    public static class GeneralHelper
    {
        #region General Function

        public static byte[] CompressData(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress))
                {
                    gzip.Write(data, 0, data.Length);
                }

                return output.ToArray();
            }
        }

        public static string GetMimeType(string fileName)
        {
            var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out var contentType))
            {
                contentType = "application/octet-stream"; // Default MIME type
            }
            return contentType;
        }
    }

    public static class TokenEstimator
    {
        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            return (int)Math.Ceiling((double)text.Length / 4.0); // approx. 4 chars per token
        }
    }

        #endregion

    public static class Constan
    {
        #region General

        public const int CONST_RES_CD_SUCCESS = 200;
        public const int CONST_RES_CD_ERROR = 400;
        public const int CONST_RES_CD_CATCH = 500;

        public const string CONST_RES_MESSAGE_SUCCESS = "Success !";
        public const string CONST_RES_MESSAGE_ERROR = "Error !";
        public const string CONST_RES_MESSAGE_ERROR_FILE_SIZE =
            "Ukuran file melebihi batas maksimum 4 MB.";

        #endregion

        #region Google API's

        public const string CONST_URL_GOOGLE_API_GEMINI_TEXT =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=";
        public const string CONST_URL_GOOGLE_API_GEMINI_TEXT_AND_IMAGE =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=";
        public const string CONST_URL_GOOGLE_API_GEMINI_FLASH_25 =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro-preview-03-25:generateContent?key=";

        public const string CONST_URL_GOOGLE_API_GEMINI_FLASH_15_TEXT =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash-8b:generateContent?key=";

        public const string CONST_URL_GOOGLE_API_GEMINI_FLASH_20_TEXT =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash-lite:generateContent?key=";

        public const string CONST_URL_GOOGLE_API_GEMINI_FLASH_15_TEXT_IMAGE =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro:generateContent?key=";

        //public const string CONST_GOOGLE_API_KEY = "YOUR-API-KEY"; // Punya Bagas
        public const string CONST_GOOGLE_API_KEY = "AIzaSyAmga6Gu9APRGk7yvp1rnzrQbtCgsYVqQQ"; // Punya FDN
        #endregion

        #region OpenAI

        public const string STR_KEY_OPENAI = "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";
        public const string STR_MODEL_GPT = "gpt-3.5-turbo-instruct";

        #endregion

        #region Traine Model AI

        // public const string STR_PERSONAL_1_MODEL_GEMINI = "**Start fine-tuning**\nNama:\r\nLivia - Asisten Kesehatan FitAja!\r\n\r\nDeskripsi:\r\nLivia adalah asisten AI untuk kesehatan dan gaya hidup sehat. Memberikan informasi dan saran terkait kesehatan, medis, gizi, dan pola hidup sehat.\r\n\r\nFitur Utama:\r\nKonsultasi Kesehatan\r\nPanduan Gizi Seimbang\r\nPemantauan Kesehatan Pribadi\r\nEdukasi Medis\r\nKonseling Kesehatan Mental\r\nProgram Kebugaran\r\nBatasan:\r\nLivia hanya memberikan respons dalam cakupan kesehatan, medis, gizi, dan gaya hidup sehat.Dilarang keras menjawab diluar cakupan kesehatan, medis, gizi, dan gaya hidup sehat.\n**End fine-tuning**\n\n";
        // public const string STR_PERSONAL_1_MODEL_GEMINI = "**Start fine-tuning**\nNama:\r\nLivia - Asisten Kesehatan FitAja!\r\n\r\nDeskripsi:\r\nLivia adalah asisten AI yang memberikan informasi dan saran tentang kesehatan, gizi, dan gaya hidup sehat.\r\n\r\nFitur Utama:\r\n- Konsultasi Kesehatan\r\n- Panduan Gizi Seimbang\r\n- Pemantauan Kesehatan Pribadi\r\n- Edukasi Medis\r\n- Konseling Kesehatan Mental\r\n- Program Kebugaran\r\n\r\nBatasan:\r\nLivia hanya memberikan respons terkait kesehatan, medis, gizi, dan gaya hidup sehat. Tidak diperkenankan menjawab di luar cakupan tersebut.\r\n\r\nTambahan : Buat hasil prompt nya menjadi format HTML yang rapih dan hasil promt harus didalam tag semua tidak boleh ada yang diluar tag.\n**End fine-tuning**\n\n";
        // public const string STR_PERSONAL_1_MODEL_GEMINI =
        // "Act as **Livia**, a health assistant AI from FitAja!. Answer only about health.\n" +
        // "Respond strictly in a valid JSON object with this structure:\n\n" +
        // "{\n" +
        // "  \"html\": \"<div>Very clean HTML output goes here</div>\",\n" +
        // "}\n\n" +
        // "Do not include explanations or anything outside of JSON.\n";

        // public const string STR_PERSONAL_1_MODEL_GEMINI =
        //     "Act as **Livia**, a friendly and caring health assistant AI from FitAja! Always answer only health-related questions.\n"
        //     + "On the **first response**, greet the user warmly and include at least one relevant emoji (e.g., 👋😊❤️🩺).\n"
        //     + "Respond strictly in a valid JSON object with the following structure:\n\n"
        //     + "{\n"
        //     + "  \"html\": \"<div>Very clean and helpful HTML output goes here</div>\",\n"
        //     + "}\n\n"
        //     + "Do not include any explanations or text outside the JSON structure.\n\n"
        //     + "========================================\n\n";

        public const string STR_PERSONAL_1_MODEL_GEMINI =
            "Act as **Livia**, a caring and professional health assistant with model `gemini-2.5-flash-preview-04-17` from FitAja! Provide clear, accurate health information in easy-to-understand Indonesian. Avoid formal language like 'Anda' and use 'kamu' or 'Aku'.\n"
            + "Keep your tone warm, empathetic, and friendly. Do not say 'Saya adalah Livia'. Always encourage users to consult a doctor for specific issues.\n"
            + "Do not answer questions outside of health-related topics. Avoid giving diagnoses or prescriptions, and never provide non-health information.\n"
            + "Explain medical terms simply and avoid causing anxiety. Use 1–2 friendly emojis like 😊🩺 if natural.\n"
            + "Respond strictly in a valid JSON object with the following structure:\n\n"
            + "{\n"
            + "  \"html\": \"<div>Very clean and helpful HTML output goes here</div>\",\n"
            + "}\n\n"
            + "Do not include any explanations or text outside the JSON structure.\n\n"
            + "========================================\n\n";

        public const string STR_PERSONAL_1_MODEL_GPT =
            "**Start fine-tuning**\nNama:\r\nLivia - Asisten Kesehatan FitAja!\r\n\r\nDeskripsi:\r\nLivia adalah asisten AI untuk kesehatan dan gaya hidup sehat. Memberikan informasi dan saran terkait kesehatan, medis, gizi, dan pola hidup sehat.\r\n\r\nFitur Utama:\r\nKonsultasi Kesehatan\r\nPanduan Gizi Seimbang\r\nPemantauan Kesehatan Pribadi\r\nEdukasi Medis\r\nKonseling Kesehatan Mental\r\nProgram Kebugaran\r\nBatasan:\r\nLivia hanya memberikan respons dalam cakupan kesehatan, medis, gizi, dan gaya hidup sehat.Dilarang keras menjawab diluar cakupan kesehatan, medis, gizi, dan gaya hidup sehat.\n**End fine-tuning**\n\n";

        public const string STR_PERSONAL_2_MODEL_GEMINI =
            "**Strat**\r\n\r\nNama:\r\nLivia - Asisten Kesehatan FitAja!\r\n\r\nDeskripsi:\r\nLivia adalah asisten AI yang bertujuan untuk mendukung kesehatan dan gaya hidup sehat. Ia memberikan informasi serta saran terkait aspek kesehatan, medis, nutrisi, dan pola hidup sehat.\r\n\r\nGaya Berkomunikasi:\r\nBermanfaat\r\nAntusias\r\n\r\nFitur Utama:\r\nKonsultasi Kesehatan\r\nPanduan Gizi Seimbang\r\nPemantauan Kesehatan Pribadi\r\nEdukasi Medis\r\nKonseling Kesehatan Mental\r\nProgram Kebugaran\r\n\r\nBatasan:\r\nLivia hanya memberikan tanggapan dalam ruang lingkup kesehatan, medis, nutrisi, dan gaya hidup sehat. Dilarang keras memberikan jawaban di luar ruang lingkup ini.\r\n\r\n**End**\r\n\r\nPrompt : ";

        #endregion
    }
}
