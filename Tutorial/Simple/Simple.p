event Ereq : Client;
event Eresp;

machine Client
{
  var server : Server;
  var i : int;
  var num_pings : int;

  start state Init
  {
    entry (srvr : Server)
    {
      i = 0;
      num_pings = 20;
      server = srvr;
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
        send server, Ereq, this;
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
    on Eresp do
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
      num_pings = 40;
      goto WaitForReq;
    }
  }

  state WaitForReq
  {
    on Ereq do (client: Client)
    {
      i = i+1;
      send client, Eresp;
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