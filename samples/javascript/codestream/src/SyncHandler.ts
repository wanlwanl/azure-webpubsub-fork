const encoding = require("lib0/dist/encoding.cjs");
const decoding = require("lib0/dist/decoding.cjs");

const syncProtocol = require("y-protocols/dist/sync.cjs");

import { Doc } from "yjs";

const messageSync = 0;

import { WebPubSubServiceClient } from "@azure/web-pubsub";
import {
  ConnectedRequest,
  ConnectionContext,
  WebPubSubEventHandler,
} from "./webpubsub-express";

import { Connection as ServerConnection } from "./SyncConnection";

function Arr2Buf(array: Uint8Array): ArrayBuffer {
  return array.buffer.slice(
    array.byteOffset,
    array.byteLength + array.byteOffset
  );
}

class WSSharedDoc extends Doc {
  conns: Map<string, ConnectionContext>;
  awareness: any;

  constructor(client: WebPubSubServiceClient) {
    super({ gc: true });
    this.conns = new Map();

    const updateHandler = (update, origin, doc: WSSharedDoc) => {
      const encoder = encoding.createEncoder();
      encoding.writeVarUint(encoder, messageSync);
      syncProtocol.writeUpdate(encoder, update);
      const message = Arr2Buf(encoding.toUint8Array(encoder));
      doc.conns.forEach((_: any, connId: string) =>
        client.sendToConnection(connId, message)
      );
    };

    this.on("update", updateHandler);
  }
}

export default class SyncHandler extends WebPubSubEventHandler {
  private _client: WebPubSubServiceClient;
  private _connections: Map<string, ServerConnection> = new Map();

  constructor(hub: string, path: string, client: WebPubSubServiceClient) {
    super(hub, {
      path: path,
      onConnected: (req: ConnectedRequest) => this.onConnected(req),
    });
    this._client = client;
  }

  getHostConnection(group: string) {
    if (!this._connections.has(group)) {
      let connection = new ServerConnection(this._client, group);
      connection.connect()
      this._connections.set(group, connection)
    }
    return this._connections.get(group)
  }

  onConnected(req: ConnectedRequest) {
  }

  async client_negotiate(req: any, res: any) {
    let group = req.query.id === undefined ? "default" : req.query.id;
    this.getHostConnection(group)

    let token = await this._client.getClientAccessToken({
      roles: [
        `webpubsub.joinLeaveGroup.${group}`,
        `webpubsub.sendToGroup.${group}.host`,
      ],
    });
    res.json({
      url: token.url,
    });
  }
}