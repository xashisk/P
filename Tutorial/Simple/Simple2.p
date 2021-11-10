type t_req = (client : Client, val : int);
type t_resp = int;

event ERequest: t_req;
event EResponse: t_resp;

machine Client
{
  var server : Server;
  var req_no : int;
  var req_val: int;
  var num_pings : int;

  start state Init
  {
    entry (srvr : Server)
    {
      req_no = 0;
      num_pings = 20;
      req_val = 1;
      server = srvr;
      goto SendReq;
    }
  }

  state SendReq
  {
    entry
    {
      if (req_no < num_pings)
      {
        req_no = req_no+1;
        send server, ERequest, (client = this, val = req_val);
        goto WaitforResp;
      }
      else
      {
        goto Exit;
      }
    }
  }

  state WaitforResp
  {
    on EResponse do (resp_val: t_resp)
    {
      req_val = resp_val;
      goto SendReq;
    }
  }

  state Exit
  {
    entry
    {
      raise halt;
    }
  }
}

machine Server
{
  var i : int;
  var num_pings : int;
  
  start state Init
  {
    entry
    {
      i = 0;
      num_pings = 40;
      goto WaitForReq;
    }
  }

  state WaitForReq
  {
    on ERequest do (req: t_req)
    {
      var resp_val: int;

      i = i+1;
      resp_val = 2 * req.val;
      send req.client, EResponse, resp_val;
      if (i >= num_pings)
      {
        raise halt;
      }
    }
  }
}

machine Main
{
  var server: Server;
  var client1: Client;
  var client2: Client;

  start state Init
  {
    entry
    {
      server = new Server();
      client1 = new Client(server);
      client2 = new Client(server);
      goto Exit;
    }
  }

  state Exit
  {
    entry
    {
      raise halt;
    }
  }
}