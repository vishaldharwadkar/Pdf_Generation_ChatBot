from fastapi import FastAPI, Request
from pydantic import BaseModel
from typing import List
from transformers import AutoTokenizer, AutoModel, pipeline
import torch
import uvicorn

app = FastAPI()

# You can replace this with the DeepSeek embedding model if available on Hugging Face
MODEL_NAME = "thenlper/gte-base"  # Example: DeepSeek or GTE-base

tokenizer = AutoTokenizer.from_pretrained(MODEL_NAME)
model = AutoModel.from_pretrained(MODEL_NAME)

class EmbedRequest(BaseModel):
    texts: List[str]

@app.post("/embed")
def embed(req: EmbedRequest):
    with torch.no_grad():
        encoded_input = tokenizer(req.texts, padding=True, truncation=True, return_tensors="pt")
        model_output = model(**encoded_input)
        embeddings = model_output.last_hidden_state[:, 0, :].cpu().numpy()
        return {"embeddings": embeddings.tolist()}

qa_pipeline = pipeline("question-answering", model="deepset/roberta-base-squad2")

class AnswerRequest(BaseModel):
    question: str
    context: str

@app.post("/answer")
def answer(req: AnswerRequest):
    result = qa_pipeline(question=req.question, context=req.context)
    return {"answer": result["answer"]}


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)


