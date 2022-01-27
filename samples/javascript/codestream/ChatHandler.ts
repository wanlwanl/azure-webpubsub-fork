import { WebPubSubServiceClient } from "@azure/web-pubsub";
import {
  WebPubSubEventHandler,
  ConnectedRequest,
  DisconnectedRequest,
  ConnectRequest,
  ConnectResponseHandler,
  UserEventRequest,
  UserEventResponseHandler,
} from "./src/webpubsub-express/index";

const ClaimTypeRole =
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const ClaimTypeName =
  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";

enum UserState {
  Host = 0,
  Active,
  Inactive,
}

class GroupUser {
  connId: string;
  state: UserState;
  user: string;

  constructor(connId: string, user: string) {
    this.connId = connId;
    this.user = user;
    this.state = UserState.Active;
  }
}

function state2status(state: UserState) {
  switch (state) {
    case UserState.Host:
      return "host";
    case UserState.Active:
      return "online";
    case UserState.Inactive:
      return "offline";
  }
}

class GroupContext {
  users: { [key: string]: GroupUser };

  name: string;

  constructor(name: string) {
    this.name = name;
    this.users = {};
  }

  host(user: string) {
    let current: GroupUser;
    let currentHost: GroupUser;

    Object.entries(this.users).forEach(([k, v]) => {
      if (k == user) {
        current = v;
      }
      if (v.state == UserState.Host) {
        currentHost = v;
      }
    });

    if (currentHost == undefined || currentHost === current) {
      current.state = UserState.Host;
      return true;
    }
    return false;
  }

  offline(user: string, connId: string) {
    Object.entries(this.users).forEach(([k, v]) => {
      if (k == user && v.connId == connId) {
        v.state = UserState.Inactive;
      }
    });
  }

  toJSON() {
    let res = {
      type: "lobby",
      users: [],
    };

    for (let [k, v] of Object.entries(this.users)) {
      res.users.push({
        connectionId: v.connId,
        name: v.user,
        status: state2status(v.state),
      });
    }
    return res;
  }
}

export default class ChatHandler extends WebPubSubEventHandler {
  client: WebPubSubServiceClient;
  groupDict: Map<string, GroupContext>;
  connectionDict: Map<string, string>;

  constructor(hub: string, path: string, client: WebPubSubServiceClient) {
    super(hub, {
      path: path,
      onConnected: (req: ConnectedRequest) => this.onConnected(req),
      onDisconnected: (req: DisconnectedRequest) => this.onDisconnected(req),
      handleConnect: (req: ConnectRequest, res: ConnectResponseHandler) =>
        this.handleConnect(req, res),
      handleUserEvent: (req: UserEventRequest, res: UserEventResponseHandler) =>
        this.handleUserEvent(req, res),
    });
    this.client = client;
    this.groupDict = new Map();
    this.connectionDict = new Map();
  }

  onConnected(req: ConnectedRequest) {
    let connId = req.context.connectionId;
    console.log(`${connId} connected`);

    let groupName = this.connectionDict[connId];
    let groupContext = this.groupDict[groupName];

    this.client
      .group(groupName)
      .addConnection(connId)
      .then(() => {
        if (groupContext != undefined) {
          this.client.group(groupName).sendToAll(groupContext.toJSON());
        }
      });
  }

  onDisconnected(req: DisconnectedRequest) {
    let connId = req.context.connectionId;
    console.log(`${connId} disconnected`);

    let groupName = this.connectionDict[connId];
    let groupContext = this.groupDict[groupName];
    groupContext?.offline(req.context.userId, req.context.connectionId);

    this.client
      .group(groupName)
      .removeConnection(connId)
      .then(() => {
        if (groupContext != undefined) {
          this.client.group(groupName).sendToAll(groupContext.toJSON());
        }
      });
  }

  handleConnect(req: ConnectRequest, res: ConnectResponseHandler) {
    let connId = req.context.connectionId;
    let claims = req.claims;
    let roles = claims[ClaimTypeRole];

    let groupName = roles[0].split(".", 3)[2];
    this.connectionDict[connId] = groupName;

    let groupContext = this.groupDict[groupName];
    let userId = claims[ClaimTypeName][0];

    groupContext.users[userId] = new GroupUser(connId, userId);
    res.success();
  }

  handleUserEvent(req: UserEventRequest, res: UserEventResponseHandler) {
    let userId = req.context.userId;

    switch (req.context.eventName) {
      case "host":
        let data: any = req.data;
        let group = data.group;
        let groupContext = this.groupDict[group];
        if (groupContext.host(userId)) {
          this.client.group(group).sendToAll(groupContext.toJSON());
        } else {
          this.client.sendToConnection(req.context.connectionId, {
            type: "message",
            data: {
              level: "warning",
              message: "There is someone else is hosting.",
            },
          });
        }
    }
    res.success();
  }

  async negotiate(req: any, res: any) {
    let group =
      req.query.id?.toString() || Math.random().toString(36).slice(2, 7);

    if (this.groupDict[group] == undefined) {
      this.groupDict[group] = new GroupContext(group);
    }

    let roles = [
      `webpubsub.sendToGroup.${group}`,
      `webpubsub.joinLeaveGroup.${group}`,
    ];

    let userId: string =
      req.user ??
      req.claims?.name ??
      "Anonymous " + Math.floor(1000 + Math.random() * 9000);

    let token = await this.client.getClientAccessToken({
      userId: userId,
      roles: roles,
    });

    res.json({
      group: group,
      user: userId,
      url: token.url,
    });
  }
}
