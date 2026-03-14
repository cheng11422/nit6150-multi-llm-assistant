import openai
import google.generativeai as genai

# API KEYS
openai.api_key = "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"
genai.configure(api_key="AIzaSyAZsN4gWjs8eN6x-HwMs_HIiB2NXKnRcfc")

DEEPSEEK_API_KEY = "sk-fe5d6b410ee54178b8e4063b9f7f51dd"


# -----------------------------
# OpenAI
# -----------------------------
def ask_openai(question):

    response = openai.ChatCompletion.create(
        model="gpt-3.5-turbo",
        messages=[{"role": "user", "content": question}]
    )

    return response["choices"][0]["message"]["content"]


# -----------------------------
# Gemini
# -----------------------------
def ask_gemini(question):

    model = genai.GenerativeModel("gemini-pro")
    response = model.generate_content(question)

    return response.text


# -----------------------------
# DeepSeek
# -----------------------------
def ask_deepseek(question):

    client = openai.OpenAI(
        api_key=DEEPSEEK_API_KEY,
        base_url="https://api.deepseek.com"
    )

    response = client.chat.completions.create(
        model="deepseek-chat",
        messages=[{"role": "user", "content": question}]
    )

    return response.choices[0].message.content


# -----------------------------
# Multi-LLM System
# -----------------------------
def multi_llm_query(question):

    answers = {}

    try:
        answers["OpenAI"] = ask_openai(question)
    except:
        answers["OpenAI"] = "Error"

    try:
        answers["Gemini"] = ask_gemini(question)
    except:
        answers["Gemini"] = "Error"

    try:
        answers["DeepSeek"] = ask_deepseek(question)
    except:
        answers["DeepSeek"] = "Error"

    return answers


# -----------------------------
# Run
# -----------------------------
if __name__ == "__main__":

    question = input("Ask a question: ")

    answers = multi_llm_query(question)

    for model, answer in answers.items():
        print(f"\n{model} Response:\n{answer}")
