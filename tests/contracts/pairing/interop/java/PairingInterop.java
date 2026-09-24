// Offline synthetic library interop only; not Android service or product wire code.
import java.math.BigInteger;
import java.security.*;
import java.io.*;
import java.util.*;
import org.bouncycastle.crypto.agreement.jpake.*;
import org.bouncycastle.crypto.digests.SHA256Digest;

public class PairingInterop {
  static final BufferedReader in=new BufferedReader(new InputStreamReader(System.in));
  public static void main(String[] args) {
    try {
      if(args.length!=2 || !Set.of("00000000","00123456","00123457").contains(args[0]) || args[1].length()!=64) System.exit(3);
      String own="pbng-pair-v1:A:"+args[1], peer="pbng-pair-v1:W:"+args[1];
      JPAKEParticipant p=new JPAKEParticipant(own,args[0].toCharArray(),JPAKEPrimeOrderGroups.NIST_3072,new SHA256Digest(),new SecureRandom());
      JPAKERound1Payload r1=p.createRound1PayloadToSend();
      send(join(u(r1.getGx1(),384),u(r1.getGx2(),384),u(r1.getKnowledgeProofForX1()[0],384),u(r1.getKnowledgeProofForX1()[1],32),u(r1.getKnowledgeProofForX2()[0],384),u(r1.getKnowledgeProofForX2()[1],32)));
      byte[] b=read(1600);
      JPAKERound1Payload remote=new JPAKERound1Payload(peer,n(b,0,384),n(b,384,384),new BigInteger[]{n(b,768,384),n(b,1152,32)},new BigInteger[]{n(b,1184,384),n(b,1568,32)});
      p.validateRound1PayloadReceived(remote);
      JPAKERound2Payload r2=p.createRound2PayloadToSend();
      send(join(u(r2.getA(),384),u(r2.getKnowledgeProofForX2s()[0],384),u(r2.getKnowledgeProofForX2s()[1],32)));
      b=read(800);p.validateRound2PayloadReceived(new JPAKERound2Payload(peer,n(b,0,384),new BigInteger[]{n(b,384,384),n(b,768,32)}));
      BigInteger key=p.calculateKeyingMaterial();
      send(s(p.createRound3PayloadToSend(key).getMacTag()));
      b=read(32);
      p.validateRound3PayloadReceived(new JPAKERound3Payload(peer,new BigInteger(b)),key);
      System.out.println("OK:"+HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(u(key,384))));
    } catch(Exception e) { System.out.println("REJECT"); System.exit(2); }
  }
  static BigInteger n(byte[] b,int offset,int size){return new BigInteger(1,Arrays.copyOfRange(b,offset,offset+size));}
  static byte[] u(BigInteger v,int size){byte[] b=v.toByteArray();if(b.length>1 && b[0]==0)b=Arrays.copyOfRange(b,1,b.length);if(b.length>size)throw new IllegalArgumentException();byte[] out=new byte[size];System.arraycopy(b,0,out,size-b.length,b.length);return out;}
  static byte[] s(BigInteger v){byte[] b=v.toByteArray();if(b.length>32)throw new IllegalArgumentException();byte[] out=new byte[32];if(v.signum()<0)Arrays.fill(out,(byte)255);System.arraycopy(b,0,out,32-b.length,b.length);return out;}
  static byte[] join(byte[]...parts)throws IOException{ByteArrayOutputStream out=new ByteArrayOutputStream();for(byte[] part:parts)out.write(part);return out.toByteArray();}
  static void send(byte[] b){System.out.println(Base64.getEncoder().encodeToString(b));}
  static byte[] read(int size)throws IOException{String line=in.readLine();if(line==null || line.length()>6000)throw new IOException();byte[] b=Base64.getDecoder().decode(line);if(b.length!=size)throw new IOException();return b;}
}
