// WaitingForEvent is the default value for execution status
// ExecutionStatus is put inside common_data; updated when
// GotoStmt is encountered

event e_req : Client;
event e_resp;

machine Client
{
  var server : Server;
  var i : int;
  var num_pings : int;

  start state Init
  {
    entry (payload : Server)
    {
      i = 0;
      num_pings = 20;
      server = payload;
      goto SendReq;
    }
  }

  state SendReq
  {
    entry
    {
      if (i < num_pings)
      {
        i = i+1;
        send server, e_req, this;
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
    on e_resp do
    {
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
      num_pings = 20;
      goto WaitForReq;
    }
  }

  state WaitForReq
  {
    on e_req do (client: Client)
    {
      send client, e_resp;
    }
  }
}