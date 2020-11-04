using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X509Certificate2Generator
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(txtCACertPass.Text) || (checkBox1.Checked && string.IsNullOrWhiteSpace(txtCAName.Text)) || (!checkBox1.Checked && string.IsNullOrWhiteSpace(txtCACertPath.Text)) || string.IsNullOrWhiteSpace(txtName.Text) || string.IsNullOrWhiteSpace(txtName.Text))
                {
                    MessageBox.Show("Make sure you fill required information to continue.");
                    return;
                }

                AsymmetricKeyParameter caprivateKey = null;
                X509Certificate2 caCert = checkBox1.Checked ? GenerateCACertificate(txtCAName.Text.Trim(), ref caprivateKey) : ImportCertificate(txtCACertPath.Text, txtCACertPass.Text);
                if (caprivateKey == null)
                {
                    caprivateKey = TransformRSAPrivateKey(caCert.PrivateKey);
                }

                X509Certificate2 cert = GenerateSelfSignedCertificate(txtName.Text.Trim(), caCert.Subject, caprivateKey);


                using (var fbd = new FolderBrowserDialog())
                {
                    DialogResult result = fbd.ShowDialog();

                    if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    {
                        SaveCert(cert, fbd.SelectedPath, cert.Subject, txtPass.Text);
                        if (checkBox1.Checked)
                        {
                            SaveCert(caCert, fbd.SelectedPath, caCert.Subject, txtCACertPass.Text);
                        }
                    }
                }


            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

        }

        public static void SaveCert(X509Certificate2 cert, string path, string name, string pass)
        {
            if (name.StartsWith("CN=", StringComparison.InvariantCultureIgnoreCase))
                name = name.Remove(0, 3);
            byte[] certData = cert.Export(X509ContentType.Pfx, pass);
            File.WriteAllBytes(Path.Combine(path, $"{name}.pfx"), certData);
            MessageBox.Show($"'{name}' Certificate Created!");
        }

        public static AsymmetricKeyParameter TransformRSAPrivateKey(AsymmetricAlgorithm privateKey)
        {
            RSACryptoServiceProvider prov = privateKey as RSACryptoServiceProvider;
            RSAParameters parameters = prov.ExportParameters(true);

            return new RsaPrivateCrtKeyParameters(
                new BigInteger(1, parameters.Modulus),
                new BigInteger(1, parameters.Exponent),
                new BigInteger(1, parameters.D),
                new BigInteger(1, parameters.P),
                new BigInteger(1, parameters.Q),
                new BigInteger(1, parameters.DP),
                new BigInteger(1, parameters.DQ),
                new BigInteger(1, parameters.InverseQ));
        }

        internal static X509Certificate2 ImportCertificate(string certPath, string pass)
        {
            X509Certificate2 x509Certificate2 = null;

            try
            {
                // Create a collection object and populate it using the PFX file
                X509Certificate2Collection collection = new X509Certificate2Collection();
                collection.Import(certPath, pass, X509KeyStorageFlags.PersistKeySet);
                x509Certificate2 = collection.Count > 0 ? collection[0] : null;
                if (x509Certificate2 != null)
                    return x509Certificate2;
                else
                    throw new Exception($"Failed to load Https certificate from the following path: {certPath}");
            }
            catch (Exception)
            {
                throw;
            }
        }


        public static X509Certificate2 GenerateSelfSignedCertificate(string subjectName, string issuerName, AsymmetricKeyParameter issuerPrivKey)
        {
            const int keyStrength = 2048;

            // Generating Random Numbers
            CryptoApiRandomGenerator randomGenerator = new CryptoApiRandomGenerator();
            SecureRandom random = new SecureRandom(randomGenerator);

            // The Certificate Generator
            X509V3CertificateGenerator certificateGenerator = new X509V3CertificateGenerator();

            // Serial Number
            BigInteger serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            // Signature Algorithm
            const string signatureAlgorithm = "SHA256WithRSA";
            certificateGenerator.SetSignatureAlgorithm(signatureAlgorithm);

            // Issuer and Subject Name
            X509Name subjectDN = new X509Name($"CN={subjectName}");
            X509Name issuerDN = new X509Name($"CN={issuerName}");
            certificateGenerator.SetIssuerDN(issuerDN);
            certificateGenerator.SetSubjectDN(subjectDN);

            // Valid For
            DateTime notBefore = DateTime.UtcNow.Date;
            DateTime notAfter = notBefore.AddYears(2);

            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            // Subject Public Key
            AsymmetricCipherKeyPair subjectKeyPair;
            var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            subjectKeyPair = keyPairGenerator.GenerateKeyPair();

            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            // Generating the Certificate
            AsymmetricCipherKeyPair issuerKeyPair = subjectKeyPair;

            // Selfsign certificate
            Org.BouncyCastle.X509.X509Certificate certificate = certificateGenerator.Generate(issuerPrivKey, random);

            // Corresponding private key
            PrivateKeyInfo info = PrivateKeyInfoFactory.CreatePrivateKeyInfo(subjectKeyPair.Private);

            // Merge into X509Certificate2
            X509Certificate2 x509 = new X509Certificate2(certificate.GetEncoded());

            Asn1Sequence seq = (Asn1Sequence)info.ParsePrivateKey();
            if (seq.Count != 9)
            {
                //throw new PemException("malformed sequence in RSA private key");
            }

            RsaPrivateKeyStructure rsa = new RsaPrivateKeyStructure(seq);
            RsaPrivateCrtKeyParameters rsaparams = new RsaPrivateCrtKeyParameters(
                rsa.Modulus, rsa.PublicExponent, rsa.PrivateExponent, rsa.Prime1, rsa.Prime2, rsa.Exponent1, rsa.Exponent2, rsa.Coefficient);

            x509.PrivateKey = DotNetUtilities.ToRSA(rsaparams);
            return x509;

        }

        public static X509Certificate2 GenerateCACertificate(string subjectName, ref AsymmetricKeyParameter CaPrivateKey)
        {
            const int keyStrength = 2048;

            // Generating Random Numbers
            CryptoApiRandomGenerator randomGenerator = new CryptoApiRandomGenerator();
            SecureRandom random = new SecureRandom(randomGenerator);

            // The Certificate Generator
            X509V3CertificateGenerator certificateGenerator = new X509V3CertificateGenerator();

            // Serial Number
            BigInteger serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            // Signature Algorithm
            const string signatureAlgorithm = "SHA256WithRSA";
            certificateGenerator.SetSignatureAlgorithm(signatureAlgorithm);

            // Issuer and Subject Name
            X509Name subjectDN = new X509Name($"CN={subjectName}");
            X509Name issuerDN = subjectDN;
            certificateGenerator.SetIssuerDN(issuerDN);
            certificateGenerator.SetSubjectDN(subjectDN);

            // Valid For
            DateTime notBefore = DateTime.UtcNow.Date;
            DateTime notAfter = notBefore.AddYears(2);

            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            // Subject Public Key
            AsymmetricCipherKeyPair subjectKeyPair;
            KeyGenerationParameters keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            RsaKeyPairGenerator keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            subjectKeyPair = keyPairGenerator.GenerateKeyPair();

            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            // Generating the Certificate
            AsymmetricCipherKeyPair issuerKeyPair = subjectKeyPair;

            // Selfsign certificate
            Org.BouncyCastle.X509.X509Certificate certificate = certificateGenerator.Generate(issuerKeyPair.Private, random);
            X509Certificate2 x509 = new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate.GetEncoded());

            CaPrivateKey = issuerKeyPair.Private;

            return x509;
            //return issuerKeyPair.Private;
        }

        public static bool addCertToStore(System.Security.Cryptography.X509Certificates.X509Certificate2 cert, System.Security.Cryptography.X509Certificates.StoreName st, System.Security.Cryptography.X509Certificates.StoreLocation sl)
        {
            bool bRet = false;

            X509Store store = new X509Store(st, sl);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);

            store.Close();
            return bRet;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                txtCAName.Enabled = true;
                txtCACertPath.Enabled = false;
                button2.Enabled = false;
            }
            else
            {
                txtCAName.Enabled = false;
                txtCACertPath.Enabled = true;
                button2.Enabled = true;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            OpenFileDialog theDialog = new OpenFileDialog();
            theDialog.Title = "Open Pfx File";
            theDialog.Filter = "PFX files|*.pfx";
            theDialog.Multiselect = false;
            if (theDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    txtCACertPath.Text = theDialog.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: Could not read file from disk. Original error: " + ex.Message);
                }
            }
        }
    }
}
