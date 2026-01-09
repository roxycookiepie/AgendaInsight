using Newtonsoft.Json;
using System;

namespace AI.Models
{
    public class AzureOpenAIModels
    {
        public class OpenAiRequest
        {
            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("temperature")]
            public double Temperature { get; set; }

            [JsonProperty("messages")]
            public Message[] Messages { get; set; }
        }
        public class Message
        {
            [JsonProperty("role")]
            public string Role { get; set; }

            [JsonProperty("content")]
            public string Content { get; set; }
        }
        //classes for openai response
        public class OpenAiChatCompletionResponse
        {
            public string id { get; set; }
            public string model { get; set; }
            public PromptFilterResult[] prompt_filter_results { get; set; }
            public Choice[] choices { get; set; }
            public Usage usage { get; set; }
            public string briefType { get; set; } //manually set
            public string projectId { get; set; } //manually set
        }
        public class PromptFilterResult
        {
            public int prompt_index { get; set; }
            public ContentFilterResults content_filter_results { get; set; }
        }
        public class ContentFilterResults
        {
            public FilterResult hate { get; set; }
            public FilterResult self_harm { get; set; }
            public FilterResult sexual { get; set; }
            public FilterResult violence { get; set; }
        }
        public class FilterResult
        {
            public bool filtered { get; set; }
            public string severity { get; set; }
        }
        public class Choice
        {
            public string finish_reason { get; set; }
            public int index { get; set; }
            public Message message { get; set; }
            public ContentFilterResults content_filter_results { get; set; }
        }
        public class Usage
        {
            public int prompt_tokens { get; set; }
            public int completion_tokens { get; set; }
            public int total_tokens { get; set; }
        }

        //parsing the parameter to test the prompt
        public class ResponseData
        {
            public string body { get; set; }
        }
    }

}