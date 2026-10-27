local id = meitou.contracts.register {
  id = "dust-bandit",
  kind = "faction",
  faction = "Dust Bandits",
  title = "Dust Bandit Cull",
  detail = "Kill two Dust Bandits for the frontier board.",
  required = 2,
  reward = 6000,
  objective = "kill",
}

meitou.log.info("registered " .. id)

meitou.events.on("bounty.accepted", function(b)
  meitou.log.info("accepted " .. b.title .. " (" .. b.id .. ")")
end)

meitou.events.on("bounty.completed", function(b)
  meitou.log.info("completed " .. b.title .. " for " .. tostring(b.reward) .. " cats")
end)
