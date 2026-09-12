# SmartChatBot (RAG-Based AI Assistant)

Ye ek full-stack AI chatbot application hai jo RAG (Retrieval-Augmented Generation) architecture par base hai. Ye hawa mein baatein (hallucinate) nahi karta, balki Supabase database mein save kiye gaye aapke private data/documents ko padh kar accurate aur context-aware answers deta hai.

### Tech Stack
* Frontend: Pure HTML/CSS/JS (Floating premium widget with real-time text streaming).
* Backend: C# .NET Web API (WebApplication4).
* Database: Supabase (PostgreSQL with pgvector extension).
* AI Engine: Ollama (Local AI models).

--------------------------------------------------

## Step-by-Step Setup Guide

### Step 1: Ollama Setup (Local AI Engine)
Ollama ka use hum local machine par bade AI models run karne ke liye karte hain.

1. Install: ollama.com se Ollama download karke install karein.
2. Models Download Karein: Apna terminal kholen aur ye dono commands chalayen:
   * Text ko vector/numbers mein badalne ke liye: ollama pull nomic-embed-text
   * Chat ka answer generate karne ke liye: ollama pull llama3
3. Verify: Terminal mein 'ollama list' type karein. Dono models wahan dikhne chahiye. (Ollama background mein http://localhost:11434 par run hota hai).

### Step 2: Supabase Database Setup
Supabase mein hum apna data aur unke vectors (embeddings) save karenge.

1. Project Create: supabase.com par naya project banayein.
2. Enable pgvector: Supabase ke "SQL Editor" mein jayen aur vector extension enable karein:
   CREATE EXTENSION IF NOT EXISTS vector;
3. Table Banayein: Data store karne ke liye table create karein:
   CREATE TABLE documents (
       id BIGSERIAL PRIMARY KEY,
       content TEXT NOT NULL,
       embedding vector(768)
   );
4. Search Function (RPC): User ke sawal se milta-julta data nikalne ke liye ye function run karein:
   CREATE OR REPLACE FUNCTION match_documents (
     query_embedding vector(768),
     match_threshold float,
     match_count int
   )
   RETURNS TABLE (id bigint, content text, similarity float)
   LANGUAGE SQL STABLE
   AS $$
     SELECT
       documents.id,
       documents.content,
       1 - (documents.embedding <=> query_embedding) AS similarity
     FROM documents
     WHERE 1 - (documents.embedding <=> query_embedding) > match_threshold
     ORDER BY documents.embedding <=> query_embedding
     LIMIT match_count;
   $$;

### Step 3: Backend Setup (.NET Web API)
Backend UI se message leta hai, vector banwata hai, data dhoondhta hai, aur AI se answer generate karwata hai.

1. Terminal mein backend folder ke andar jayen: cd Backend/WebApplication4
2. Zaroori NuGet packages install karein:
   dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
   dotnet add package Pgvector.EntityFrameworkCore
3. Apni appsettings.json file mein Supabase ka connection string dalein.
4. Project run karein:
   dotnet run
   (API https://localhost:7021 par run hone lagegi).

### Step 4: Frontend Setup
1. Frontend folder ke andar majood index.html file ko kisi bhi browser (Chrome/Edge) mein open karein.
2. Floating chat button par click karein.
3. Chatbot typing effect ke sath "Hi! How can I help you today?" bolega. 
4. Koi bhi message type karke Send karein aur real-time streaming answer enjoy karein!

--------------------------------------------------

## Security & Git Rules (Bahut Zaroori)
GitHub par code push karte waqt API keys leak na hon, isliye repository ke root mein .gitignore file banakar usme ye lines zaroor add karein:

bin/
obj/
**/bin/
**/obj/
appsettings.json

(Isse build files aur secrets kabhi GitHub par push nahi honge aur GitHub Secret Scanning aapko block nahi karega).

--------------------------------------------------

## Project Kaam Kaise Karta Hai (The RAG Flow)?
1. User UI par sawal likhta hai.
2. C# Backend us sawal ko Ollama (nomic-embed-text) ko bhej kar uska vector banwata hai.
3. Backend wo vector Supabase ko bhejta hai aur sabse relevant paragraphs nikal kar lata hai.
4. Backend user ka sawal aur database se mila relevant data (context) dono Ollama (llama3) ko bhejta hai.
5. AI answer generate karta hai aur usko UI par chunk-by-chunk stream kar deta hai.
