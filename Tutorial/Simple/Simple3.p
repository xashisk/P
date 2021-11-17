event e_cquery : Coordinator;
event e_yes;
event e_no;
event e_commit;
event e_rollback;
event e_ack;

machine Coordinator
{
	var participants : seq[Participant];
  var count_yes : int;
  var count: int;

	start state Init
  {
		entry (payload: seq[Participant])
    {
			var i : int;

			participants = payload;
      i = 0;

      while (i < sizeof(participants))
      {
        send participants[i], e_cquery, this;
        i = i+1;
      }

			goto WaitForVotes;
		}
	}

	state WaitForVotes
  {
    entry
    {
      count_yes = 0;
    }

    on e_yes do
    {
      count_yes = count_yes + 1;

      if (count_yes == sizeof(participants))
      {
        goto CommitAll;
      }
    }

    on e_no do
    {
      goto RollbackAll;
    }
	}

  state CommitAll
  {
    entry
    {
      var i: int;
      
      i = 0;
      while (i < sizeof(participants))
      {
        send participants[i], e_commit;
        i = i+1;
      }
      goto WaitForAcks;
    }
  }

  state RollbackAll
  {
    entry
    {
      var i: int;
      
      i = 0;
      while (i < sizeof(participants))
      {
        send participants[i], e_rollback;
        i = i+1;
      }
      goto WaitForAcks;
    }
  }

	state WaitForAcks
  {
    entry
    {
      count = 0;
    }

    on e_ack do
    {
      count = count + 1;

      if (count == sizeof(participants))
      {
        raise halt;
      }
    }
	}
}

machine Participant
{
  var coordinator: Coordinator;

  start state Init
  {
	  entry
    {
			goto WaitForCQuery;
		}
	}

	state WaitForCQuery
  {
		on e_cquery do (e_cquery : Coordinator)
    {
      coordinator = e_cquery;
      goto SendVote;
		}
  }

  state SendVote
  {
    entry
    {
      var vote : event;

      vote = e_yes;
      send coordinator, vote;

      goto WaitForTxn;
    }
  }

  state WaitForTxn
  {
    on e_commit do
    {
      // some commitment code
      goto SendAck;
    }
    on e_rollback do
    {
      // some rollback code
      goto SendAck;
    }
  }

  state SendAck
  {
    entry
    {
      send coordinator, e_ack;
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

machine Main
{
  var p0: Participant;
  var p1: Participant;
  var p2: Participant;
  var c: Coordinator;

  start state Init
  {
    entry
    {
      var s: seq[Participant];

      p0 = new Participant();
      p1 = new Participant();
      p2 = new Participant();
      
      s += (0, p0);
      s += (1, p1);
      s += (2, p2);

      c = new Coordinator(s);

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