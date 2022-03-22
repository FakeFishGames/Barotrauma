BAROTRAUMA

http://www.barotraumagame.com

© 2018-2022 FakeFish Ltd. All rights reserved.
© 2019-2022 Daedalic Entertainment GmbH. The Daedalic logo is a trademark of Daedalic Entertainment GmbH, Germany. All rights reserved.
Privacy policy: http://privacypolicy.daedalic.com

See the wiki for more detailed info and instructions:
http://barotraumagame.com/wiki

------------------------------------------------------------------------

Port forwarding:
You may try to forward ports on your router using UPnP (Universal Plug and 
Play) port forwarding by selecting "Attempt UPnP port forwarding" in the
"Host Server" menu. 

However, UPnP isn't supported by all routers, so you may need to setup port 
forwards manually. The exact steps for forwarding a port depend on your
router's model, but you may be able to find a port forwarding guide for 
your particular router/application on portforward.com or by practicing 
your google-fu skills.

These are the values that you should use when forwarding a port to your
Barotrauma server:

Game port (used to communicate with clients)
	Service/Application: barotrauma
	External Port: The port you have selected for your server (27015 by default)
	Internal Port: The port you have selected for your server (27015 by default)
	Protocol: UDP
	
Query port (used to communicate with Steam)
	Service/Application: barotrauma
	External Port: The port you have selected for your server (27016 by default)
	Internal Port: The port you have selected for your server (27016 by default)
	Protocol: UDP