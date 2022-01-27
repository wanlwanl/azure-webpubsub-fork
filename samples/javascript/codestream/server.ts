import cors from "cors";
import express from "express";

import { WebPubSubServiceClient } from "@azure/web-pubsub";

import ChatHandler from "./ChatHandler";
import SyncHandler from "./SyncHandler";

import Builder from "./src/aad-express-middleware";

const aadJwtMiddleware = new Builder().build({
  tenantId: "72f988bf-86f1-41af-91ab-2d7cd011db47",
  audience: [
    "ee79ab73-0c3a-4e1e-b8a6-46f0e8753c8b", // dev
    "b5bf00fe-b842-4c6f-b5bd-e4a7aec02b91", // prod
  ],
});

let defaultConnectionString = "Endpoint=https://newcodestream.webpubsub.azure.com;AccessKey=KtpvcUkqko8bOdWpLJeeNDpyoA+8qE1aQLuKmBhEwl4=;Version=1.0;"

let connectionString =
  process.argv[2] ||
  process.env.WebPubSubConnectionString ||
  defaultConnectionString;

let chatClient: WebPubSubServiceClient = new WebPubSubServiceClient(
  connectionString ?? "",
  "codestream"
);
let chatHandler = new ChatHandler(
  "codestream",
  "/api/webpubsub/hubs/codestream",
  chatClient
);

let syncClient: WebPubSubServiceClient = new WebPubSubServiceClient(
  connectionString ?? "",
  "yjs"
);
let syncHandler = new SyncHandler("yjs", "/api/webpubsub/hubs/yjs", syncClient);

const app = express();
app.use(chatHandler.getMiddleware());
app.use(syncHandler.getMiddleware());

let corsOptions = {
  // origin: "https://newcodestrdev60af4ctab.z5.web.core.windows.net"
};

const corsMiddleware = cors(corsOptions);
app.options("/negotiate", corsMiddleware);
app.get("/negotiate", [corsMiddleware, aadJwtMiddleware], (req, res) => chatHandler.negotiate(req, res));

app.options("/yjs/negotiate", corsMiddleware);
app.get("/yjs/negotiate", corsMiddleware, (req, res) => syncHandler.negotiate(req, res));

app.use(express.static("public"));
app.listen(8080, () => console.log("app started"));
